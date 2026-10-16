// Streaming on Universal-3.6 Pro with Medical Mode and speaker labels, from a WAV file replayed in real time.
//
//   dotnet run --project samples/Streaming -- samples/audio/visit.wav
//   dotnet run --project samples/Streaming -- samples/audio/visit.wav --keyterms "lisinopril,atorvastatin"
//
// Prints each turn as it's finalized, then the transcript rebuilt from per-word speaker labels, with the server's
// end-of-session speaker revision applied. Requires ASSEMBLYAI_API_KEY.

using System.Diagnostics;
using AssemblyAI.Reference;
using AssemblyAI.Reference.Audio;
using AssemblyAI.Reference.Streaming;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Streaming <file.wav> [--keyterms \"a,b\"]");
    return 1;
}

var wav = WavFile.Read(args[0]);
if (wav.Channels != 1)
{
    Console.Error.WriteLine("Send mono audio: one stream per connection. For two-channel calls, open one session per channel.");
    return 1;
}

var keyterms = Option(args, "--keyterms")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

using var client = new AssemblyAIClient(AssemblyAIOptions.FromEnvironment());

var parameters = new StreamingParameters
{
    SpeechModel = StreamingSpeechModels.Universal36Pro,
    SampleRate = wav.SampleRate,
    Domain = "medical-v1",   // Medical Mode corrects final turns
    // Speaker labels switch turn detection to values tuned for diarization (640 / 768 ms), so turns end when the
    // speaker changes. Leave MinTurnSilence and MaxTurnSilence unset here: values you set replace those, and at the
    // 800 ms recommended for medical audio, a quick handover between doctor and patient stays inside one turn.
    // For one clinician dictating without speaker labels, use 800 / 3600 ms.
    SpeakerLabels = true,
    MaxSpeakers = 3,
    KeytermsPrompt = keyterms,
};

await using var session = await client.Streaming.ConnectAsync(parameters);
Console.WriteLine($"Session {session.Begin.Id} started. Server configuration: {session.Begin.Configuration}");

var assembler = new TranscriptAssembler();
TerminationMessage? termination = null;

// Read on its own task: the server streams turns back while we're still sending audio.
var reader = Task.Run(async () =>
{
    await foreach (var message in session.ReadMessagesAsync())
    {
        assembler.Apply(message);
        switch (message)
        {
            case TurnMessage { EndOfTurn: true } turn:
                Console.WriteLine($"[{turn.SpeakerLabel ?? "?"}] {turn.Transcript}");
                break;
            case SpeakerRevisionMessage revision:
                Console.WriteLine($"(speaker revision: {revision.Revisions.Count} turn(s) relabeled)");
                break;
            case ErrorMessage error:
                Console.Error.WriteLine($"Error {error.ErrorCode}: {error.Error}");
                break;
            case TerminationMessage end:
                termination = end;
                break;
        }
    }
});

// 50 ms frames at real-time pace, the way a microphone would produce them.
var frame = TimeSpan.FromMilliseconds(50);
var pacer = Stopwatch.StartNew();
var sent = TimeSpan.Zero;
foreach (var chunk in wav.Frames(frame))
{
    await session.SendAudioAsync(chunk);
    sent += frame;
    var ahead = sent - pacer.Elapsed;
    if (ahead > TimeSpan.Zero)
    {
        await Task.Delay(ahead);
    }
}

// Always terminate: streaming bills for as long as the session is open.
await session.TerminateAsync();
await reader;

Console.WriteLine();
Console.WriteLine("Final transcript (revised speaker labels):");
Console.Write(assembler.ToText());
if (termination is not null)
{
    Console.WriteLine($"Audio {termination.AudioDurationSeconds}s, session {termination.SessionDurationSeconds}s (billed on session time).");
}
else
{
    Console.WriteLine($"Session closed without Termination: {session.CloseStatus} {session.CloseStatusDescription}");
}

return 0;

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
