// Dictation API: send audio while the clinician is still talking and get back the verbatim transcript plus a
// rewrite in one call.
//
//   dotnet run --project samples/Dictation -- samples/audio/dictation.wav
//   dotnet run --project samples/Dictation -- samples/audio/dictation.wav --spoken-punctuation
//   dotnet run --project samples/Dictation -- samples/audio/dictation.wav --instruction "Rewrite as a concise clinical chart note."
//   dotnet run --project samples/Dictation -- samples/audio/dictation.wav --recording
//
// Live mode (the default) replays the WAV at real-time speed through a ProducerStream, the way a microphone
// would feed it, and measures how long the result takes once "speech" ends. --recording sends the finished file
// to the buffered route instead. Requires ASSEMBLYAI_API_KEY.

using System.Diagnostics;
using AssemblyAI.Reference;
using AssemblyAI.Reference.Audio;
using AssemblyAI.Reference.Dictation;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Dictation <file.wav> [--recording] [--spoken-punctuation] [--instruction \"...\"] [--keyterms \"a,b\"]");
    return 1;
}

const string SpokenPunctuation =
    "Apply the speaker's dictation commands: replace \"comma\", \"period\", \"colon\" and \"question mark\" with " +
    "the punctuation they name, \"new line\" with a line break, and \"new paragraph\" with a blank line. Keep every " +
    "other word exactly as spoken.";

var path = args[0];
var recording = args.Contains("--recording");
var instruction = args.Contains("--spoken-punctuation") ? SpokenPunctuation : Option(args, "--instruction");
var keyterms = Option(args, "--keyterms")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

using var client = new AssemblyAIClient(AssemblyAIOptions.FromEnvironment());

DictationResult result;
var clock = new Stopwatch();

if (recording)
{
    await using var file = File.OpenRead(path);
    clock.Start();
    result = await client.Dictation.TranscribeRecordingAsync(file, "audio/wav", new DictationConfig
    {
        LanguageCodes = new[] { "en" },
        SttPrompt = "A doctor dictating a patient visit note.",
        KeytermsPrompt = keyterms,
        LlmInstruction = instruction,
    });
}
else
{
    var wav = WavFile.Read(path);

    // Open the connection while the user starts talking, so TLS setup isn't on the critical path.
    await client.Dictation.WarmAsync();

    var microphone = new ProducerStream();
    var pending = client.Dictation.TranscribeLiveAsync(microphone, LiveAudioFormat.Pcm, new DictationConfig
    {
        SampleRate = wav.SampleRate,
        Channels = wav.Channels,
        LanguageCodes = new[] { "en" },
        SttPrompt = "A doctor dictating a patient visit note.",
        KeytermsPrompt = keyterms,
        LlmInstruction = instruction,
    });

    Console.WriteLine($"Dictating {wav.Duration.TotalSeconds:F1}s of audio in real time...");
    var frame = TimeSpan.FromMilliseconds(100);
    var pacer = Stopwatch.StartNew();
    var sent = TimeSpan.Zero;
    foreach (var chunk in wav.Frames(frame))
    {
        // If the request has already failed (for example the connection dropped), stop feeding audio.
        if (pending.IsCompleted)
        {
            break;
        }

        await microphone.WriteAsync(chunk);
        sent += frame;
        var ahead = sent - pacer.Elapsed;
        if (ahead > TimeSpan.Zero)
        {
            await Task.Delay(ahead);
        }
    }

    clock.Start(); // "speech" has ended; everything after this is wait time the clinician feels
    microphone.Complete();
    result = await pending;
}

clock.Stop();

Console.WriteLine();
Console.WriteLine("Verbatim (text):");
Console.WriteLine(result.Text);
Console.WriteLine();
Console.WriteLine(instruction is null ? "Default cleanup (llm_response):" : "Your instruction (llm_response):");
Console.WriteLine(result.LlmResponse ?? $"(none: {result.LlmError ?? "no rewrite returned"})");
Console.WriteLine();
Console.WriteLine(recording
    ? $"Round trip: {clock.ElapsedMilliseconds} ms for {result.AudioDurationMs} ms of audio."
    : $"Result arrived {clock.ElapsedMilliseconds} ms after the audio ended ({result.AudioDurationMs} ms of audio).");

return 0;

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
