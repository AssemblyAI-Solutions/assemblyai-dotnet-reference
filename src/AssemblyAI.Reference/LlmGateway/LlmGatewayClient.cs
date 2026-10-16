using System.Text;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.Async;
using AssemblyAI.Reference.Http;
using AssemblyAI.Reference.SpeechUnderstanding;

namespace AssemblyAI.Reference.LlmGateway;

/// <summary>
/// LLM Gateway: chat completions across providers, and Speech Understanding on an existing transcript.
/// Docs: https://www.assemblyai.com/docs/llm-gateway/quickstart
/// </summary>
public sealed class LlmGatewayClient
{
    private readonly HttpClient _http;
    private readonly AssemblyAIOptions _options;

    public LlmGatewayClient(HttpClient http, AssemblyAIOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary><c>POST /v1/chat/completions</c>.</summary>
    public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ChatCompletionResponse>("/v1/chat/completions", request, cancellationToken);

    /// <summary>
    /// <c>POST /v1/understanding</c>: runs Speech Understanding (for example Speaker Identification) on a transcript
    /// you already have. The transcript needs speaker labels. The result comes back in the response only; it is not
    /// written back to the transcript.
    /// </summary>
    public Task<UnderstandingResponse> RunSpeechUnderstandingAsync(
        string transcriptId,
        SpeechUnderstandingRequest speechUnderstanding,
        CancellationToken cancellationToken = default) =>
        PostAsync<UnderstandingResponse>(
            "/v1/understanding",
            new UnderstandingRequest(transcriptId, speechUnderstanding),
            cancellationToken);

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.LlmGatewayBaseUri, path))
        {
            Content = new StringContent(AssemblyAIJson.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ApiErrors.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<T>(json);
    }

    private sealed record UnderstandingRequest(
        [property: JsonPropertyName("transcript_id")] string TranscriptId,
        [property: JsonPropertyName("speech_understanding")] SpeechUnderstandingRequest SpeechUnderstanding);
}

public sealed class UnderstandingResponse
{
    [JsonPropertyName("speech_understanding")]
    public SpeechUnderstandingResult? SpeechUnderstanding { get; init; }

    /// <summary>The transcript's utterances with <c>speaker</c> replaced by the identified role or name.</summary>
    [JsonPropertyName("utterances")]
    public IReadOnlyList<TranscriptUtterance>? Utterances { get; init; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}
