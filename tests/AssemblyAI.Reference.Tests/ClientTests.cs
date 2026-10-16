using System.Net;
using System.Text.Json;
using AssemblyAI.Reference.Async;
using AssemblyAI.Reference.Dictation;
using AssemblyAI.Reference.Http;
using AssemblyAI.Reference.LlmGateway;
using AssemblyAI.Reference.SpeechUnderstanding;
using AssemblyAI.Reference.Streaming;
using AssemblyAI.Reference.Webhooks;

namespace AssemblyAI.Reference.Tests;

public class TranscriptClientTests
{
    private static readonly AssemblyAIOptions Options = new() { ApiKey = "test-key" };

    private static (AssemblyAIClient Client, FakeHandler Fake) Create(AssemblyAIOptions? options = null)
    {
        var fake = new FakeHandler();
        return (new AssemblyAIClient(options ?? Options, new HttpClient(fake)), fake);
    }

    [Fact]
    public void Request_serializes_with_api_field_names_and_omits_unset_fields()
    {
        var request = new TranscriptRequest
        {
            AudioUrl = "https://example.com/visit.wav",
            SpeechModels = SpeechModels.Recommended,
            Domain = Domains.Medical,
            SpeakerLabels = true,
            SpeakerOptions = new SpeakerOptions { MinSpeakersExpected = 2, MaxSpeakersExpected = 3 },
            KeytermsPrompt = new[] { "lisinopril" },
            SpeechUnderstanding = SpeechUnderstandingRequest.IdentifySpeakersByRole(
                SpeakerProfile.ForRole("Provider"), SpeakerProfile.ForRole("Patient")),
            AdditionalProperties = new() { ["brand_new_param"] = true },
        };

        using var json = JsonDocument.Parse(AssemblyAIJson.Serialize(request));
        var root = json.RootElement;

        Assert.Equal("https://example.com/visit.wav", root.GetProperty("audio_url").GetString());
        Assert.Equal(new[] { "universal-3-5-pro", "universal-2" }, root.GetProperty("speech_models").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("medical-v1", root.GetProperty("domain").GetString());
        Assert.Equal(2, root.GetProperty("speaker_options").GetProperty("min_speakers_expected").GetInt32());
        Assert.True(root.GetProperty("brand_new_param").GetBoolean());

        var identification = root.GetProperty("speech_understanding").GetProperty("request").GetProperty("speaker_identification");
        Assert.Equal("role", identification.GetProperty("speaker_type").GetString());
        Assert.Equal("Provider", identification.GetProperty("speakers")[0].GetProperty("role").GetString());

        Assert.False(root.TryGetProperty("prompt", out _));
        Assert.False(root.TryGetProperty("language_detection", out _));
        Assert.False(identification.GetProperty("speakers")[0].TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Submit_posts_json_with_the_raw_api_key()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.OK, """{"id":"t1","status":"processing"}""");

        var transcript = await client.Transcripts.SubmitAsync(new TranscriptRequest { AudioUrl = "https://example.com/a.wav" });

        var sent = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.assemblyai.com/v2/transcript", sent.Uri.ToString());
        Assert.Equal("test-key", sent.Headers["Authorization"]);
        Assert.StartsWith("application/json", sent.ContentType);
        Assert.Equal("t1", transcript.Id);
    }

    [Fact]
    public async Task Eu_region_uses_the_eu_host()
    {
        var (client, fake) = Create(new AssemblyAIOptions { ApiKey = "k", Region = AssemblyAIRegion.EU });
        fake.Respond(HttpStatusCode.OK, """{"id":"t1","status":"completed"}""");

        await client.Transcripts.GetAsync("t1");

        Assert.Equal("https://api.eu.assemblyai.com/v2/transcript/t1", fake.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Upload_streams_bytes_and_returns_the_upload_url()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.OK, """{"upload_url":"https://cdn.assemblyai.com/upload/abc"}""");

        var url = await client.Transcripts.UploadAsync(new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.Equal("https://cdn.assemblyai.com/upload/abc", url);
        Assert.Equal("https://api.assemblyai.com/v2/upload", fake.Requests[0].Uri.ToString());
        Assert.Equal("application/octet-stream", fake.Requests[0].ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, fake.Requests[0].Body);
    }

    [Fact]
    public async Task Waits_until_completed()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.OK, """{"id":"t1","status":"queued"}""")
            .Respond(HttpStatusCode.OK, """{"id":"t1","status":"processing"}""")
            .Respond(HttpStatusCode.OK, """{"id":"t1","status":"completed","text":"Hello."}""");

        var polling = new PollingOptions { InitialInterval = TimeSpan.FromMilliseconds(1), MaxInterval = TimeSpan.FromMilliseconds(2) };
        var transcript = await client.Transcripts.WaitForCompletionAsync("t1", polling);

        Assert.True(transcript.IsCompleted);
        Assert.Equal("Hello.", transcript.Text);
        Assert.Equal(3, fake.Requests.Count);
    }

    [Fact]
    public async Task Transcribe_throws_when_the_job_errors()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.OK, """{"id":"t1","status":"processing"}""")
            .Respond(HttpStatusCode.OK, """{"id":"t1","status":"error","error":"Audio file contains no speech"}""");

        var polling = new PollingOptions { InitialInterval = TimeSpan.FromMilliseconds(1) };
        var ex = await Assert.ThrowsAsync<TranscriptFailedException>(() =>
            client.Transcripts.TranscribeAsync(new TranscriptRequest { AudioUrl = "https://example.com/a.wav" }, polling));

        Assert.Equal("Audio file contains no speech", ex.Message);
    }

    [Fact]
    public async Task Api_errors_surface_the_api_message()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.BadRequest, """{"error":"Invalid endpoint schema, please refer to documentation for examples."}""");

        var ex = await Assert.ThrowsAsync<AssemblyAIApiException>(() =>
            client.Transcripts.SubmitAsync(new TranscriptRequest { AudioUrl = "https://example.com/a.wav" }));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Invalid endpoint schema", ex.Message);
    }

    [Fact]
    public async Task Delete_uses_the_transcript_route()
    {
        var (client, fake) = Create();
        fake.Respond(HttpStatusCode.OK, """{"id":"t1","status":"completed"}""");

        await client.Transcripts.DeleteAsync("t1");

        Assert.Equal(HttpMethod.Delete, fake.Requests[0].Method);
        Assert.Equal("https://api.assemblyai.com/v2/transcript/t1", fake.Requests[0].Uri.ToString());
    }

    [Fact]
    public void Transcript_reads_utterances_speaker_identification_and_keeps_unknown_fields()
    {
        var transcript = AssemblyAIJson.Deserialize<Transcript>("""
            {
              "id": "t1", "status": "completed", "audio_duration": 49, "speech_model_used": "universal-3-5-pro",
              "utterances": [{"speaker": "Provider", "text": "What brings you in?", "start": 0, "end": 1200, "confidence": 0.98, "channel": null,
                              "words": [{"text": "What", "start": 0, "end": 200, "confidence": 0.99, "speaker": "Provider"}]}],
              "speech_understanding": {"response": {"speaker_identification": {"mapping": {"A": "Provider", "B": "Patient"}, "status": "success"}}},
              "metadata": {"warnings": [{"message": "heads up"}]},
              "some_future_field": 42
            }
            """);

        Assert.Equal("Provider", transcript.Utterances![0].Speaker);
        Assert.Equal("Patient", transcript.SpeechUnderstanding!.Response!.SpeakerIdentification!.Mapping!["B"]);
        Assert.Equal("heads up", transcript.Metadata!.Warnings![0].Message);
        Assert.Equal(42, transcript.AdditionalProperties!["some_future_field"].GetInt32());
    }
}

public class DictationClientTests
{
    private static readonly AssemblyAIOptions Options = new() { ApiKey = "test-key" };

    [Fact]
    public async Task Live_route_sends_config_before_audio_with_the_part_names_the_server_expects()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.OK, """{"text":"hello period","llm_response":"Hello."}""");
        var client = new AssemblyAIClient(Options, new HttpClient(fake));

        var result = await client.Dictation.TranscribeLiveAsync(
            new MemoryStream(new byte[] { 0, 0, 1, 0 }),
            LiveAudioFormat.Pcm,
            new DictationConfig { SampleRate = 16000, Channels = 1, SttPrompt = "A doctor dictating a patient visit note." });

        var sent = Assert.Single(fake.Requests);
        Assert.Equal("https://dictation.us.assemblyai.com/v1/transcribe/live", sent.Uri.ToString());
        Assert.Equal("test-key", sent.Headers["Authorization"]);
        Assert.StartsWith("multipart/form-data", sent.ContentType);

        var body = sent.BodyText;
        var configAt = body.IndexOf("name=\"config\"", StringComparison.Ordinal);
        var audioAt = body.IndexOf("name=\"audio\"", StringComparison.Ordinal);
        Assert.True(configAt >= 0 && audioAt > configAt, "config must come before audio");
        Assert.Contains("filename=\"audio\"", body);
        Assert.Contains("Content-Type: audio/pcm", body);
        Assert.Contains("\"sample_rate\":16000", body);
        Assert.Contains("\"stt_prompt\":\"A doctor dictating a patient visit note.\"", body);

        Assert.Equal("Hello.", result.FinalText);
    }

    [Fact]
    public async Task Pcm_without_sample_rate_is_rejected_before_any_request()
    {
        var client = new AssemblyAIClient(Options, new HttpClient(new FakeHandler()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Dictation.TranscribeLiveAsync(new MemoryStream(), LiveAudioFormat.Pcm, new DictationConfig()));
    }

    [Fact]
    public async Task Recording_route_uses_the_buffered_endpoint()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.OK, """{"text":"hello","llm_response":null,"llm_error":"timeout"}""");
        var client = new AssemblyAIClient(new AssemblyAIOptions { ApiKey = "k", Region = AssemblyAIRegion.EU }, new HttpClient(fake));

        var result = await client.Dictation.TranscribeRecordingAsync(new MemoryStream(new byte[] { 1 }), "audio/mpeg");

        Assert.Equal("https://dictation.eu.assemblyai.com/v1/transcribe", fake.Requests[0].Uri.ToString());
        Assert.Contains("Content-Type: audio/mpeg", fake.Requests[0].BodyText);
        Assert.Equal("hello", result.FinalText); // falls back to the verbatim text when the rewrite fails
        Assert.Equal("timeout", result.LlmError);
    }

    [Fact]
    public async Task Problem_details_errors_are_readable()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.BadRequest, """{"status":400,"title":"Bad Request","detail":"invalid config part: domain: Extra inputs are not permitted"}""");
        var client = new AssemblyAIClient(Options, new HttpClient(fake));

        var ex = await Assert.ThrowsAsync<AssemblyAIApiException>(() =>
            client.Dictation.TranscribeRecordingAsync(new MemoryStream(new byte[] { 1 }), "audio/wav"));

        Assert.Equal("Bad Request: invalid config part: domain: Extra inputs are not permitted", ex.Message);
    }
}

public class StreamingTests
{
    [Fact]
    public void Lists_are_sent_as_one_json_array_string_not_repeated_keys()
    {
        var query = new StreamingParameters
        {
            Domain = "medical-v1",
            SpeakerLabels = true,
            MinTurnSilence = 800,
            KeytermsPrompt = new[] { "lisinopril", "atorvastatin 20 mg" },
            LanguageCodes = new[] { "en" },
        }.ToQueryString();

        var pairs = query.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        Assert.Equal("universal-3-6-pro", pairs["speech_model"]);
        Assert.Equal("16000", pairs["sample_rate"]);
        Assert.Equal("pcm_s16le", pairs["encoding"]);
        Assert.Equal("true", pairs["speaker_labels"]);
        Assert.Equal("800", pairs["min_turn_silence"]);
        Assert.Equal("[\"lisinopril\",\"atorvastatin 20 mg\"]", pairs["keyterms_prompt"]);
        Assert.Equal("[\"en\"]", pairs["language_codes"]);
        Assert.Single(query.Split('&'), p => p.StartsWith("keyterms_prompt=", StringComparison.Ordinal));
        Assert.False(pairs.ContainsKey("prompt"));
    }

    [Fact]
    public void Connect_url_pins_the_region()
    {
        var client = new AssemblyAIClient(new AssemblyAIOptions { ApiKey = "k" });
        var uri = client.Streaming.BuildUri(new StreamingParameters(), token: "abc");

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("streaming.us.assemblyai.com", uri.Host);
        Assert.Equal("/v3/ws", uri.AbsolutePath);
        Assert.EndsWith("&token=abc", uri.Query);
    }

    [Fact]
    public void Parses_each_message_type()
    {
        Assert.IsType<BeginMessage>(StreamingMessage.Parse("""{"type":"Begin","id":"s1","expires_at":1792000000}"""));
        var turn = Assert.IsType<TurnMessage>(StreamingMessage.Parse("""
            {"type":"Turn","turn_order":0,"turn_is_formatted":true,"end_of_turn":true,"transcript":"Start aspirin.",
             "speaker_label":"A","words":[{"text":"Start","start":0,"end":300,"confidence":0.9,"word_is_final":true,"speaker":"A"}]}
            """));
        Assert.Equal("A", turn.SpeakerLabel);
        Assert.Equal(300, turn.Words[0].End);
        Assert.IsType<SpeakerRevisionMessage>(StreamingMessage.Parse("""{"type":"SpeakerRevision","revisions":[]}"""));
        Assert.IsType<TerminationMessage>(StreamingMessage.Parse("""{"type":"Termination","audio_duration_seconds":49,"session_duration_seconds":51}"""));
        Assert.Equal(3006, Assert.IsType<ErrorMessage>(StreamingMessage.Parse("""{"type":"Error","error_code":3006,"error":"bad"}""")).ErrorCode);
        Assert.Equal("Heartbeat", Assert.IsType<UnknownMessage>(StreamingMessage.Parse("""{"type":"Heartbeat"}""")).Type);
    }

    [Fact]
    public void Assembler_replaces_partials_and_applies_speaker_revisions()
    {
        var assembler = new TranscriptAssembler();
        assembler.Apply(StreamingMessage.Parse("""{"type":"Turn","turn_order":0,"end_of_turn":false,"transcript":"What brings"}"""));
        assembler.Apply(StreamingMessage.Parse("""{"type":"Turn","turn_order":0,"end_of_turn":true,"turn_is_formatted":true,"transcript":"What brings you in today?","speaker_label":"A"}"""));
        assembler.Apply(StreamingMessage.Parse("""{"type":"Turn","turn_order":1,"end_of_turn":true,"turn_is_formatted":true,"transcript":"Chest tightness.","speaker_label":"A"}"""));
        assembler.Apply(StreamingMessage.Parse("""{"type":"SpeakerRevision","revisions":[{"turn_order":1,"speaker_label":"B","words":[]}]}"""));

        var text = assembler.ToText();

        Assert.Equal($"Speaker A: What brings you in today?{Environment.NewLine}Speaker B: Chest tightness.{Environment.NewLine}", text);
        Assert.DoesNotContain("What brings What brings", text);
    }

    [Fact]
    public void Update_configuration_sends_keyterms_as_a_real_json_array()
    {
        var json = AssemblyAIJson.Serialize(new StreamingConfigurationUpdate { KeytermsPrompt = new[] { "metoprolol" }, MinTurnSilence = 1200 });

        Assert.Equal("""{"type":"UpdateConfiguration","keyterms_prompt":["metoprolol"],"min_turn_silence":1200}""", json);
    }
}

public class LlmGatewayAndWebhookTests
{
    [Fact]
    public async Task Chat_completion_posts_to_the_gateway_and_reads_the_first_choice()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.OK, """
            {"request_id":"r1","choices":[{"message":{"role":"assistant","content":"S: chest tightness"},"finish_reason":"stop"}],
             "usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}
            """);
        var client = new AssemblyAIClient(new AssemblyAIOptions { ApiKey = "k" }, new HttpClient(fake));

        var response = await client.LlmGateway.CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Model = "claude-sonnet-4-6",
            Messages = new[] { ChatMessage.User("Write a SOAP note.") },
            MaxTokens = 2000,
        });

        Assert.Equal("https://llm-gateway.assemblyai.com/v1/chat/completions", fake.Requests[0].Uri.ToString());
        Assert.Equal("k", fake.Requests[0].Headers["Authorization"]);
        Assert.Contains("\"max_tokens\":2000", fake.Requests[0].BodyText);
        Assert.Equal("S: chest tightness", response.Text);
        Assert.Equal(15, response.Usage!.TotalTokens);
    }

    [Fact]
    public async Task Understanding_route_wraps_the_transcript_id()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.OK, """
            {"speech_understanding":{"response":{"speaker_identification":{"mapping":{"A":"Provider"},"status":"success"}}},
             "utterances":[{"speaker":"Provider","text":"Hi","start":0,"end":100,"confidence":1}],"request_id":"r2"}
            """);
        var client = new AssemblyAIClient(new AssemblyAIOptions { ApiKey = "k" }, new HttpClient(fake));

        var response = await client.LlmGateway.RunSpeechUnderstandingAsync(
            "t1", SpeechUnderstandingRequest.IdentifySpeakersByRole(SpeakerProfile.ForRole("Provider"), SpeakerProfile.ForRole("Patient")));

        using var body = JsonDocument.Parse(fake.Requests[0].BodyText);
        Assert.Equal("t1", body.RootElement.GetProperty("transcript_id").GetString());
        Assert.Equal("role", body.RootElement.GetProperty("speech_understanding").GetProperty("request").GetProperty("speaker_identification").GetProperty("speaker_type").GetString());
        Assert.Equal("Provider", response.Utterances![0].Speaker);
    }

    [Theory]
    [InlineData("s3cret", "s3cret", true)]
    [InlineData("s3cret", "S3cret", false)]
    [InlineData(null, "s3cret", false)]
    [InlineData("s3cre", "s3cret", false)]
    public void Webhook_auth_header_check(string? received, string expected, bool valid) =>
        Assert.Equal(valid, WebhookAuthentication.IsValid(received, expected));

    [Fact]
    public void Webhook_payload_binds_from_the_api_field_names()
    {
        var notification = AssemblyAIJson.Deserialize<WebhookNotification>("""{"transcript_id":"t1","status":"completed"}""");

        Assert.Equal("t1", notification.TranscriptId);
        Assert.Equal("completed", notification.Status);
    }
}
