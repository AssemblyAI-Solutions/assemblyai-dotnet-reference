# AssemblyAI .NET reference implementation

Calling AssemblyAI from .NET with `HttpClient`, `ClientWebSocket` and `System.Text.Json`, without an SDK. It covers:

- **Pre-recorded transcription** on Universal-3.5 Pro with Medical Mode, speaker diarization and Speaker Identification
- **The Dictation API**, uploading while the clinician is still talking
- **Streaming** on Universal-3.6 Pro with Medical Mode and real-time speaker labels
- **LLM Gateway** for drafting notes from a transcript
- **Webhooks**, with a minimal receiver

The archived C# SDK predates most of the current API. Its request types can't express `speech_models`, `keyterms_prompt`, `prompt`, `domain` or `speech_understanding`, and it has no Dictation or current Streaming support. The API itself is plain HTTP, JSON and WebSocket, so a thin client is all you need. Every request model here also has an `AdditionalProperties` escape hatch, so you can send a new parameter the day it ships instead of waiting on a library.

The library has no package dependencies and targets `net8.0`, so it runs on .NET 8, 9 and 10. Copy the parts you need into your codebase, or reference the project.

## Quick start

```bash
export ASSEMBLYAI_API_KEY=your_key      # ASSEMBLYAI_REGION=EU for the EU data zone (needs an EU key)

dotnet test                             # unit tests, no network

dotnet run --project samples/AsyncScribe -- samples/audio/visit.wav --soap-note
dotnet run --project samples/Dictation  -- samples/audio/dictation.wav
dotnet run --project samples/Streaming  -- samples/audio/visit.wav
```

The audio in `samples/audio` is synthetic, generated with text-to-speech, so it contains no patient data.

| Path | What it shows |
| --- | --- |
| `src/AssemblyAI.Reference/Async` | Upload, submit, poll, fetch and delete pre-recorded transcripts |
| `src/AssemblyAI.Reference/Dictation` | Both Dictation routes, including streaming the upload while recording |
| `src/AssemblyAI.Reference/Streaming` | WebSocket session, typed messages, transcript assembly, temporary tokens |
| `src/AssemblyAI.Reference/LlmGateway` | Chat completions and Speech Understanding on an existing transcript |
| `src/AssemblyAI.Reference/Webhooks` | Webhook payload and constant-time auth header check |
| `src/AssemblyAI.Reference/Http` | Retry policy and error parsing shared by every client |
| `samples/*` | One runnable console app per product, plus an ASP.NET Core webhook receiver |

## Using the library

```csharp
using var client = new AssemblyAIClient(AssemblyAIOptions.FromEnvironment());

var audioUrl = await client.Transcripts.UploadFileAsync("visit.wav");

var transcript = await client.Transcripts.TranscribeAsync(new TranscriptRequest
{
    AudioUrl = audioUrl,
    SpeechModels = SpeechModels.Recommended,      // ["universal-3-5-pro", "universal-2"]
    LanguageCode = "en_us",
    Domain = Domains.Medical,                     // "medical-v1"
    SpeakerLabels = true,
    SpeakerOptions = new SpeakerOptions { MinSpeakersExpected = 2, MaxSpeakersExpected = 3 },
    SpeechUnderstanding = SpeechUnderstandingRequest.IdentifySpeakersByRole(
        SpeakerProfile.ForRole("Provider"), SpeakerProfile.ForRole("Patient")),
});

foreach (var u in transcript.Utterances!)
    Console.WriteLine($"{u.Speaker}: {u.Text}");    // "Provider: ...", "Patient: ..."

await client.Transcripts.DeleteAsync(transcript.Id); // once you've stored the result
```

`AssemblyAIClient` shares one `HttpClient` across products; create one per process. If you use `IHttpClientFactory`, register a client with the retry handler in its pipeline and pass it in:

```csharp
services.AddHttpClient("assemblyai")
        .AddHttpMessageHandler(() => new RetryHandler());
services.AddSingleton(sp => new AssemblyAIClient(
        AssemblyAIOptions.FromEnvironment(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("assemblyai")));
```

Authentication is the raw API key in the `Authorization` header, with no `Bearer` prefix. The clients add it per request rather than as a default header, so the key is never sent to another host.

## Pre-recorded transcription

- **Always set `speech_models`.** When it's omitted, the server default depends on when your account was created, so pin it to keep results comparable across environments. `["universal-3-5-pro", "universal-2"]` runs Universal-3.5 Pro for its languages and falls back to Universal-2 for the rest. The completed transcript's `speech_model_used` tells you which ran. [Docs](https://www.assemblyai.com/docs/pre-recorded-audio/select-the-speech-model)
- **[Medical Mode](https://www.assemblyai.com/docs/pre-recorded-audio/medical-mode)** is `domain: "medical-v1"`. It applies to English, Spanish, German and French. For any other language it's skipped, with a note in `metadata.warnings`, and not billed.
- **[Speaker diarization](https://www.assemblyai.com/docs/pre-recorded-audio/label-speakers)**: set `speaker_labels: true`, and either `speakers_expected` or `speaker_options` (not both). A min/max range handles visits where a family member joins.
- **[Speaker Identification](https://www.assemblyai.com/docs/speech-understanding/speaker-identification)** relabels `A`/`B` as roles or names from what each person says. It needs speaker labels. The mapping comes back in `speech_understanding.response.speaker_identification`, and `utterances[].speaker` holds the role. To relabel a transcript you already have, use `LlmGatewayClient.RunSpeechUnderstandingAsync`.
- **[Keyterms](https://www.assemblyai.com/docs/pre-recorded-audio/universal-3-5-pro/prompting#keyterms-prompting)** (`keyterms_prompt`, up to 1,000 words on Universal-3.5 Pro) work alongside Medical Mode. Start without them, then add the drug, procedure and provider names the model misses on your audio. `prompt` describes the audio and can be combined with keyterms.
- **Polling vs. webhooks.** `WaitForCompletionAsync` starts at 3 seconds, backs off to 15 seconds and adds jitter. At production volume, use [webhooks](https://www.assemblyai.com/docs/pre-recorded-audio/webhooks) and keep a slower poll as a fallback. Excess jobs are queued rather than rejected. See [transcribing at scale](https://www.assemblyai.com/docs/pre-recorded-audio/guides/bulk-transcription-and-load-tests-at-scale).
- **Retention.** [`DELETE /v2/transcript/{id}`](https://www.assemblyai.com/docs/pre-recorded-audio/delete-transcripts) removes the transcript, its outputs and any audio uploaded through `/v2/upload` immediately. Call it as soon as you've stored the result, rather than relying on the retention TTL.
- **Errors.** Non-2xx responses throw `AssemblyAIApiException` with the API's message. A transcript that finishes with `status: "error"` makes `TranscribeAsync` throw `TranscriptFailedException`. Unknown top-level request fields are rejected with a 400, so a typo fails loudly.

### What gets retried

`RetryHandler` retries:

- **429 on any method.** The request was rejected before it was processed, so sending it again is safe.
- **5xx and network failures on GET, HEAD and DELETE only.** A retried `POST /v2/transcript` after a 5xx could create a second job and bill twice.
- **Never a request whose body is a stream**, such as file uploads or live dictation audio.

It honors `Retry-After` and otherwise backs off exponentially with jitter.

## Dictation API

[Docs](https://www.assemblyai.com/docs/dictation). One request returns the verbatim transcript in `text` and a rewrite in `llm_response`.

| Route | Method | Use it for | Audio |
| --- | --- | --- | --- |
| `/v1/transcribe/live` | `TranscribeLiveAsync` | Audio still being recorded, uploaded as it's captured | WAV or raw 16-bit PCM, up to 120 seconds |
| `/v1/transcribe` | `TranscribeRecordingAsync` | A finished recording | WAV, PCM, AAC, MP3, M4A, Ogg/Opus, FLAC or WebM, up to 20 minutes |

- **Uploading while recording.** Feed microphone frames into a `ProducerStream` and pass it to `TranscribeLiveAsync`. The request starts right away, and most of the audio has already been uploaded when the clinician stops talking. Call `Complete()` when recording ends. The live route needs the `config` part before `audio`, which the client always does. [Docs](https://www.assemblyai.com/docs/dictation/uploading-while-recording)
- **Raw PCM** needs `sample_rate` and `channels` in the config. Use 16-bit little-endian PCM, mono or stereo.
- **Clinical vocabulary.** Medical Mode isn't a Dictation option, and an unknown config field such as `domain` is rejected with a 400. Use `stt_prompt` to describe the audio (for example "A doctor dictating a patient visit note.") and `keyterms_prompt` for up to 100 terms. [Docs](https://www.assemblyai.com/docs/dictation/prompting-and-keyterms)
- **The rewrite.** By default, cleanup removes fillers, resolves self-corrections, and applies punctuation and capitalization, keeping the wording as spoken. To get a different shape, set `llm_instruction` and describe only the transformation, for example "Rewrite as a concise clinical chart note." `llm_response` is null if the rewrite was skipped or failed (`llm_error` says why). `FinalText` falls back to `text`. [Docs](https://www.assemblyai.com/docs/dictation/transcript-rewriting)
- **Spoken punctuation.** The default cleanup keeps words as spoken. To turn dictated commands like "comma" and "period" into punctuation, say so in `llm_instruction`; the sample's `--spoken-punctuation` flag shows one way. Test it on your own clinicians' audio.
- **Latency.** Call `WarmAsync()` (`GET /warm`) when recording starts, so the connection is open before the real request. [Docs](https://www.assemblyai.com/docs/dictation/connection-pre-warming)

## Streaming

[Docs](https://www.assemblyai.com/docs/streaming/getting-started/transcribe-streaming-audio).

- **Connecting.** `StreamingClient.ConnectAsync` opens `wss://streaming.us.assemblyai.com/v3/ws` with your API key and returns once the server sends `Begin`. Authentication happens after the WebSocket upgrade, so a successful upgrade alone doesn't mean the key was accepted.
- **Browsers and devices.** Mint a [temporary token](https://www.assemblyai.com/docs/streaming/authenticate-with-a-temporary-token) on your server with `CreateTemporaryTokenAsync` and connect with `ConnectWithTokenAsync`, so the API key never leaves your backend.
- **Parameters** go in the URL query. List parameters such as `keyterms_prompt` and `language_codes` are sent as one JSON array string. The server keeps only the first value of a repeated key, so `keyterms_prompt=a&keyterms_prompt=b` would silently drop every term after the first. Unknown parameters are ignored, so check `Begin.Configuration` to confirm what was applied.
- **[Medical audio](https://www.assemblyai.com/docs/streaming/medical-mode).**
  - `domain=medical-v1` corrects final turns, so a turn's final text can differ from its last partial.
  - Clinicians pause mid-sentence. Raise `min_turn_silence` to 800 ms, and `max_turn_silence` to about 2,000 ms for a real-time scribe or 3,600 ms for dictation. [Turn detection](https://www.assemblyai.com/docs/streaming/turn-detection)
- **Audio.** Send binary frames of 20 to 1,000 ms; about 50 ms (1,600 bytes at 16 kHz mono) is recommended. Send in real time. Send one mono stream per connection; for two-channel calls, open one session per channel.
- **Turns.** Every `Turn` message re-transcribes the whole turn, so replace what you hold for that `turn_order` rather than appending. The turn is final when `end_of_turn` is true. `TranscriptAssembler` does this for you.
- **[Speaker labels](https://www.assemblyai.com/docs/streaming/label-speakers-and-separate-channels).**
  - `speaker_labels=true` labels final turns. A turn too short to attribute comes back as `PENDING`.
  - At the end of the session the server re-checks labels over the whole conversation and sends `SpeakerRevision` for the turns that changed. Use the revised labels for anything you store.
  - `speaker_labels_revision_interval_ms=300000` also sends revisions every 5 minutes.
  - Turning on speaker labels also changes the turn-detection defaults, so set the silence values explicitly.
- **Ending the session.** Call `TerminateAsync()`, then keep reading until `Termination`, which arrives after the last turn and any revision. Streaming bills on how long the session is open, not on audio duration, so don't leave idle sockets open.
- **Mid-session changes.** `UpdateConfigurationAsync` changes settings such as keyterms or turn silence. Here `keyterms_prompt` is a real JSON array. Avoid sending `mode` mid-session: it re-applies that mode's whole profile and overwrites earlier silence settings.
- **[Close codes](https://www.assemblyai.com/docs/streaming/common-session-errors-and-closures).**
  - 1008: authentication failed, or too many new sessions per minute.
  - 3006: invalid parameter or message.
  - 3007: audio frame too short or too long.
  - 3008: the session reached its maximum duration (3 hours by default).

## LLM Gateway

`CreateChatCompletionAsync` posts an OpenAI-shaped chat completion to `https://llm-gateway.assemblyai.com/v1/chat/completions`. The AsyncScribe sample's `--soap-note` flag drafts a SOAP note from the speaker-attributed utterances with `claude-sonnet-4-6`, the model our [medical scribe guide](https://www.assemblyai.com/docs/medical-scribe-best-practices) uses. For how inputs and outputs are retained per provider, see [data retention](https://www.assemblyai.com/docs/data-retention-and-model-training). [Docs](https://www.assemblyai.com/docs/llm-gateway/chat-completions)

## Webhooks

- **What arrives.** When a job finishes, AssemblyAI POSTs `{"transcript_id", "status"}` to your `webhook_url`. The body carries no transcript text.
- **Authenticating it.** Set `webhook_auth_header_name` and `webhook_auth_header_value` and check the header on arrival. `WebhookAuthentication.IsValid` compares in constant time.
- **Responding.** Return a 2xx within 10 seconds, then fetch the transcript. Slower responses count as failures and are retried.
- **Allowlisting.** Requests come from `44.238.19.20` (US) and `54.220.25.36` (EU).
- **Sample.** `samples/WebhookReceiver` acknowledges immediately, then fetches, stores and deletes the transcript on a background worker. It logs only IDs and counts, never transcript text. [Docs](https://www.assemblyai.com/docs/pre-recorded-audio/webhooks)

## Regions

`AssemblyAIOptions.Region` pins every product to one data zone. US is the default; EU uses `api.eu`, `streaming.eu`, `dictation.eu` and `llm-gateway.eu`. EU endpoints need an API key created in the EU. [Pre-recorded](https://www.assemblyai.com/docs/pre-recorded-audio/select-the-region) · [Streaming](https://www.assemblyai.com/docs/streaming/endpoints-and-data-zones) · [Dictation](https://www.assemblyai.com/docs/dictation/endpoints-and-data-zones)
