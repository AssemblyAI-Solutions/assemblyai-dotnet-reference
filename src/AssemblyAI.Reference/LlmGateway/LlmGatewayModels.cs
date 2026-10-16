using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssemblyAI.Reference.LlmGateway;

/// <summary>
/// Body for <c>POST /v1/chat/completions</c>. The route follows the OpenAI chat completions shape.
/// Docs: https://www.assemblyai.com/docs/llm-gateway/chat-completions
/// </summary>
public sealed class ChatCompletionRequest
{
    /// <summary>Model id, for example <c>claude-sonnet-4-6</c> (the model our medical scribe guide uses).</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Defaults to 1000 on the server.</summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>
    /// Optional transcript to inject: the transcript's <c>text</c> replaces the first <c>{{ transcript }}</c>
    /// in your messages. Note that <c>text</c> carries no speaker labels.
    /// </summary>
    [JsonPropertyName("transcript_id")]
    public string? TranscriptId { get; init; }

    /// <summary>Any parameter this model doesn't cover yet (for example <c>response_format</c> or <c>tools</c>).</summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? AdditionalProperties { get; init; }
}

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    public static ChatMessage System(string content) => new("system", content);

    public static ChatMessage User(string content) => new("user", content);
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatChoice> Choices { get; init; } = Array.Empty<ChatChoice>();

    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }

    /// <summary>The first choice's text, which is what a single-answer request needs.</summary>
    [JsonIgnore]
    public string? Text => Choices.Count > 0 ? Choices[0].Message?.Content : null;
}

public sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatResponseMessage? Message { get; init; }

    /// <summary><c>stop</c> or <c>length</c>. <c>length</c> means the answer was cut off at <c>max_tokens</c>.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class ChatResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

public sealed class ChatUsage
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
