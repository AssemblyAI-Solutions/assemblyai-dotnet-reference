using System.Text.Json;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Streaming;

/// <summary>Base type for every JSON message the server sends. Switch on the concrete type.</summary>
public abstract class StreamingMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    /// <summary>Parses one text frame into its typed message, or <see cref="UnknownMessage"/> for types this library doesn't know yet.</summary>
    public static StreamingMessage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        var options = AssemblyAIJson.Options;
        return type switch
        {
            "Begin" => JsonSerializer.Deserialize<BeginMessage>(json, options)!,
            "Turn" => JsonSerializer.Deserialize<TurnMessage>(json, options)!,
            "SpeechStarted" => JsonSerializer.Deserialize<SpeechStartedMessage>(json, options)!,
            "SpeakerRevision" => JsonSerializer.Deserialize<SpeakerRevisionMessage>(json, options)!,
            "Termination" => JsonSerializer.Deserialize<TerminationMessage>(json, options)!,
            "Error" => JsonSerializer.Deserialize<ErrorMessage>(json, options)!,
            _ => new UnknownMessage { Type = type ?? "", Raw = doc.RootElement.Clone() },
        };
    }
}

/// <summary>First message of every session. Receiving it is the signal that authentication succeeded.</summary>
public sealed class BeginMessage : StreamingMessage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Unix time (seconds) when the session's maximum duration is reached.</summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }

    /// <summary>The configuration the server actually applied (model, mode, domain, speaker labels and so on).</summary>
    [JsonPropertyName("configuration")]
    public JsonElement? Configuration { get; init; }
}

/// <summary>
/// One update for a turn. Every <c>Turn</c> re-transcribes the whole turn, so a client must REPLACE what it holds
/// for that <see cref="TurnOrder"/>, never append. The turn is final when <see cref="EndOfTurn"/> is true.
/// Medical Mode and speaker labels apply to the final, so its text can differ from the last partial.
/// </summary>
public sealed class TurnMessage : StreamingMessage
{
    [JsonPropertyName("turn_order")]
    public int TurnOrder { get; init; }

    [JsonPropertyName("turn_is_formatted")]
    public bool TurnIsFormatted { get; init; }

    [JsonPropertyName("end_of_turn")]
    public bool EndOfTurn { get; init; }

    [JsonPropertyName("transcript")]
    public string Transcript { get; init; } = "";

    [JsonPropertyName("end_of_turn_confidence")]
    public double? EndOfTurnConfidence { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<StreamingWord> Words { get; init; } = Array.Empty<StreamingWord>();

    /// <summary>Empty until the turn's final message.</summary>
    [JsonPropertyName("utterance")]
    public string? Utterance { get; init; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; init; }

    [JsonPropertyName("language_confidence")]
    public double? LanguageConfidence { get; init; }

    /// <summary><c>A</c>, <c>B</c>, ... on final turns when speaker labels are on, or <c>PENDING</c> for a turn too short to attribute.</summary>
    [JsonPropertyName("speaker_label")]
    public string? SpeakerLabel { get; init; }

    [JsonPropertyName("speaker_confidence")]
    public double? SpeakerConfidence { get; init; }
}

public sealed class StreamingWord
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Milliseconds from the start of the session's audio.</summary>
    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonPropertyName("end")]
    public long End { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("word_is_final")]
    public bool WordIsFinal { get; init; }

    [JsonPropertyName("speaker")]
    public string? Speaker { get; init; }

    [JsonPropertyName("speaker_confidence")]
    public double? SpeakerConfidence { get; init; }
}

public sealed class SpeechStartedMessage : StreamingMessage
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

/// <summary>
/// Corrected speaker labels for turns you already received. The end-of-session revision runs over the whole
/// conversation, so use these labels for anything you store or analyze. Each message only carries turns that changed.
/// </summary>
public sealed class SpeakerRevisionMessage : StreamingMessage
{
    [JsonPropertyName("revisions")]
    public IReadOnlyList<SpeakerRevision> Revisions { get; init; } = Array.Empty<SpeakerRevision>();
}

public sealed class SpeakerRevision
{
    [JsonPropertyName("turn_order")]
    public int TurnOrder { get; init; }

    [JsonPropertyName("speaker_label")]
    public string? SpeakerLabel { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<StreamingWord> Words { get; init; } = Array.Empty<StreamingWord>();
}

/// <summary>Always the last message. Streaming bills on session duration, not audio duration.</summary>
public sealed class TerminationMessage : StreamingMessage
{
    [JsonPropertyName("audio_duration_seconds")]
    public double AudioDurationSeconds { get; init; }

    [JsonPropertyName("session_duration_seconds")]
    public double SessionDurationSeconds { get; init; }
}

/// <summary>Sent just before the server closes the socket with an error code.</summary>
public sealed class ErrorMessage : StreamingMessage
{
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>A message type this library doesn't model yet (for example opt-in heartbeats).</summary>
public sealed class UnknownMessage : StreamingMessage
{
    [JsonIgnore]
    public JsonElement Raw { get; init; }
}
