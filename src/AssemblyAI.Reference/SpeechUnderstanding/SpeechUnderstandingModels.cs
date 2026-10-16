using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssemblyAI.Reference.SpeechUnderstanding;

/// <summary>
/// The <c>speech_understanding</c> block. The same shape works inline on <c>POST /v2/transcript</c> and on the
/// standalone LLM Gateway route <c>POST /v1/understanding</c>.
/// Docs: https://www.assemblyai.com/docs/speech-understanding/speaker-identification
/// </summary>
public sealed class SpeechUnderstandingRequest
{
    [JsonPropertyName("request")]
    public required SpeechUnderstandingTasks Request { get; init; }

    /// <summary>Speaker Identification by role, for example clinician and patient.</summary>
    public static SpeechUnderstandingRequest IdentifySpeakersByRole(params SpeakerProfile[] speakers) => new()
    {
        Request = new SpeechUnderstandingTasks
        {
            SpeakerIdentification = new SpeakerIdentification { SpeakerType = "role", Speakers = speakers },
        },
    };
}

public sealed class SpeechUnderstandingTasks
{
    [JsonPropertyName("speaker_identification")]
    public SpeakerIdentification? SpeakerIdentification { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? AdditionalProperties { get; init; }
}

/// <summary>
/// Replaces <c>A</c>/<c>B</c> speaker labels with names or roles inferred from what each speaker says.
/// Needs speaker labels on the transcript. Each name or role can be up to 35 characters.
/// </summary>
public sealed class SpeakerIdentification
{
    /// <summary><c>role</c> or <c>name</c>.</summary>
    [JsonPropertyName("speaker_type")]
    public required string SpeakerType { get; init; }

    [JsonPropertyName("speakers")]
    public IReadOnlyList<SpeakerProfile>? Speakers { get; init; }

    /// <summary><c>low</c> (default) or <c>medium</c>, for conversations where the roles are harder to tell apart. Medium costs more.</summary>
    [JsonPropertyName("effort")]
    public string? Effort { get; init; }
}

/// <summary>One expected speaker. Set <see cref="Role"/> or <see cref="Name"/> to match <c>speaker_type</c>.</summary>
public sealed class SpeakerProfile
{
    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Optional hint, for example "Asks about symptoms and prescribes medication".</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    public static SpeakerProfile ForRole(string role, string? description = null) => new() { Role = role, Description = description };
}

/// <summary>The <c>speech_understanding</c> block on a completed transcript or a <c>/v1/understanding</c> response.</summary>
public sealed class SpeechUnderstandingResult
{
    [JsonPropertyName("response")]
    public SpeechUnderstandingResponse? Response { get; init; }
}

public sealed class SpeechUnderstandingResponse
{
    [JsonPropertyName("speaker_identification")]
    public SpeakerIdentificationResult? SpeakerIdentification { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class SpeakerIdentificationResult
{
    /// <summary>Original label to identified role or name, for example <c>{"A": "Clinician", "B": "Patient"}</c>.</summary>
    [JsonPropertyName("mapping")]
    public IReadOnlyDictionary<string, string>? Mapping { get; init; }

    /// <summary><c>success</c> or <c>failure</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("effort")]
    public string? Effort { get; init; }
}
