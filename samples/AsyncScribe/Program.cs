// Pre-recorded medical scribe: upload a visit recording, transcribe it with Universal-3.5 Pro + Medical Mode,
// label who is the provider and who is the patient, optionally draft a SOAP note, then delete the transcript.
//
//   dotnet run --project samples/AsyncScribe -- samples/audio/visit.wav
//   dotnet run --project samples/AsyncScribe -- samples/audio/visit.wav --keyterms "lisinopril,atorvastatin" --soap-note
//   dotnet run --project samples/AsyncScribe -- https://example.com/visit.mp3 --keep
//
// Requires ASSEMBLYAI_API_KEY. Set ASSEMBLYAI_REGION=EU to use the EU data zone.

using AssemblyAI.Reference;
using AssemblyAI.Reference.Async;
using AssemblyAI.Reference.LlmGateway;
using AssemblyAI.Reference.SpeechUnderstanding;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: AsyncScribe <audio file or URL> [--keyterms \"a,b,c\"] [--soap-note] [--keep]");
    return 1;
}

var source = args[0];
var keyterms = Option(args, "--keyterms")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var soapNote = args.Contains("--soap-note");
var keep = args.Contains("--keep");

using var client = new AssemblyAIClient(AssemblyAIOptions.FromEnvironment());

// 1. Local files go through /v2/upload; anything else is treated as a URL AssemblyAI can fetch.
var audioUrl = File.Exists(source) ? await client.Transcripts.UploadFileAsync(source) : source;

// 2. The scribe configuration. Every option here is a plain JSON field on POST /v2/transcript.
var request = new TranscriptRequest
{
    AudioUrl = audioUrl,
    SpeechModels = SpeechModels.Recommended, // always explicit: ["universal-3-5-pro", "universal-2"]
    LanguageCode = "en_us",
    Domain = Domains.Medical,                // Medical Mode
    SpeakerLabels = true,
    SpeakerOptions = new SpeakerOptions { MinSpeakersExpected = 2, MaxSpeakersExpected = 3 },
    KeytermsPrompt = keyterms,               // add the drug and procedure names the model misses on your audio
    SpeechUnderstanding = SpeechUnderstandingRequest.IdentifySpeakersByRole(
        SpeakerProfile.ForRole("Provider", "Asks about symptoms, examines the patient and prescribes treatment"),
        SpeakerProfile.ForRole("Patient", "Describes their symptoms, history and medications")),
};

// 3. Submit, then poll. SubmitAsync returns as soon as the job is queued; WaitForCompletionAsync polls
//    GET /v2/transcript/{id} (3 s at first, backing off to 15 s, with jitter) until it's completed or errored.
//    In production, store submitted.Id with your record: the job keeps running on AssemblyAI's side, so after a
//    restart you can call WaitForCompletionAsync with the stored id and pick up where you left off.
var submitted = await client.Transcripts.SubmitAsync(request);
Console.WriteLine($"Submitted {submitted.Id}. Polling for the result...");

var transcript = await client.Transcripts.WaitForCompletionAsync(submitted.Id);
if (transcript.IsError)
{
    Console.Error.WriteLine($"Transcription failed: {transcript.Error}");
    return 1;
}

Console.WriteLine($"Transcript {transcript.Id}: {transcript.AudioDuration}s of audio, model {transcript.SpeechModelUsed}");
foreach (var warning in transcript.Metadata?.Warnings ?? Array.Empty<TranscriptWarning>())
{
    Console.WriteLine($"Warning: {warning.Message}");
}

var identification = transcript.SpeechUnderstanding?.Response?.SpeakerIdentification;
if (identification?.Mapping is { } mapping)
{
    Console.WriteLine($"Speaker Identification ({identification.Status}): {string.Join(", ", mapping.Select(m => $"{m.Key} -> {m.Value}"))}");
}

Console.WriteLine();
var utterances = transcript.Utterances ?? Array.Empty<TranscriptUtterance>();
foreach (var utterance in utterances)
{
    Console.WriteLine($"[{TimeSpan.FromMilliseconds(utterance.Start):mm\\:ss}] {utterance.Speaker}: {utterance.Text}");
}

// 4. Optional: draft a SOAP note through LLM Gateway, from the speaker-attributed utterances.
if (soapNote)
{
    var dialogue = string.Join("\n", utterances.Select(u => $"{u.Speaker}: {u.Text}"));
    var completion = await client.LlmGateway.CreateChatCompletionAsync(new ChatCompletionRequest
    {
        Model = "claude-sonnet-4-6",
        MaxTokens = 2000,
        Messages = new[]
        {
            ChatMessage.System("You are a medical scribe. Write a SOAP note (Subjective, Objective, Assessment, Plan) from the visit transcript. Use only facts stated in the transcript."),
            ChatMessage.User(dialogue),
        },
    });

    Console.WriteLine();
    Console.WriteLine("SOAP note:");
    Console.WriteLine(completion.Text);
}

// 5. Delete the transcript (and the uploaded audio) as soon as you've stored what you need.
if (!keep)
{
    await client.Transcripts.DeleteAsync(transcript.Id);
    Console.WriteLine();
    Console.WriteLine($"Deleted transcript {transcript.Id}.");
}

return 0;

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
