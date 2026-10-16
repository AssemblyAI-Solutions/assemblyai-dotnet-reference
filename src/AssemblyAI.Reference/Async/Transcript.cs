using System.Text.Json;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.SpeechUnderstanding;

namespace AssemblyAI.Reference.Async;

/// <summary>
/// The transcript resource returned by <c>POST /v2/transcript</c> and <c>GET /v2/transcript/{id}</c>.
/// Fields this type doesn't model land in <see cref="AdditionalProperties"/> instead of being dropped.
/// </summary>
public sealed class Transcript
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary><c>queued</c>, <c>processing</c>, <c>completed</c> or <c>error</c>. See <see cref="TranscriptStatus"/>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<TranscriptWord>? Words { get; init; }

    /// <summary>Present when speaker labels or multichannel are on. After Speaker Identification, <c>speaker</c> holds the role or name.</summary>
    [JsonPropertyName("utterances")]
    public IReadOnlyList<TranscriptUtterance>? Utterances { get; init; }

    /// <summary>Audio length in seconds, rounded up.</summary>
    [JsonPropertyName("audio_duration")]
    public double? AudioDuration { get; init; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; init; }

    [JsonPropertyName("language_confidence")]
    public double? LanguageConfidence { get; init; }

    /// <summary>The model that actually ran, for example <c>universal-3-5-pro</c>. Null until the job finishes.</summary>
    [JsonPropertyName("speech_model_used")]
    public string? SpeechModelUsed { get; init; }

    [JsonPropertyName("entities")]
    public IReadOnlyList<TranscriptEntity>? Entities { get; init; }

    [JsonPropertyName("metadata")]
    public TranscriptMetadata? Metadata { get; init; }

    [JsonPropertyName("speech_understanding")]
    public SpeechUnderstandingResult? SpeechUnderstanding { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }

    [JsonIgnore]
    public bool IsCompleted => Status == TranscriptStatus.Completed;

    [JsonIgnore]
    public bool IsError => Status == TranscriptStatus.Error;

    /// <summary>True once the job can't change anymore (completed or error).</summary>
    [JsonIgnore]
    public bool IsFinished => IsCompleted || IsError;
}

public static class TranscriptStatus
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Error = "error";
}

public sealed class TranscriptWord
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>Milliseconds from the start of the audio.</summary>
    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonPropertyName("end")]
    public long End { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("speaker")]
    public string? Speaker { get; init; }

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    [JsonPropertyName("speaker_confidence")]
    public double? SpeakerConfidence { get; init; }
}

public sealed class TranscriptUtterance
{
    [JsonPropertyName("speaker")]
    public string? Speaker { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonPropertyName("end")]
    public long End { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    [JsonPropertyName("speaker_confidence")]
    public double? SpeakerConfidence { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<TranscriptWord>? Words { get; init; }
}

public sealed class TranscriptEntity
{
    [JsonPropertyName("entity_type")]
    public string EntityType { get; init; } = "";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonPropertyName("end")]
    public long End { get; init; }
}

public sealed class TranscriptMetadata
{
    /// <summary>Non-fatal notes, for example Medical Mode being skipped for an unsupported language.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<TranscriptWarning>? Warnings { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class TranscriptWarning
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
