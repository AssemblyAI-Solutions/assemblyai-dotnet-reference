using System.Text.Json.Serialization;
using AssemblyAI.Reference.SpeechUnderstanding;

namespace AssemblyAI.Reference.Async;

/// <summary>
/// Body for <c>POST /v2/transcript</c>. Only the properties you set are sent.
/// Docs: https://www.assemblyai.com/docs/pre-recorded-audio/api-reference/transcripts/submit
/// <para>
/// <see cref="AdditionalProperties"/> lets you send any parameter this type doesn't model yet, so a new API
/// feature never waits on a library release. The API rejects unknown top-level fields with a 400, so a typo fails
/// loudly rather than being ignored.
/// </para>
/// </summary>
public sealed class TranscriptRequest
{
    /// <summary>A URL AssemblyAI can fetch, or the <c>upload_url</c> returned by <c>POST /v2/upload</c>.</summary>
    [JsonPropertyName("audio_url")]
    public required string AudioUrl { get; init; }

    /// <summary>
    /// Models to use, in priority order. Always set this explicitly: when it's omitted, the server default depends
    /// on when your account was created. <c>["universal-3-5-pro", "universal-2"]</c> runs Universal-3.5 Pro for its
    /// languages and falls back to Universal-2 for everything else.
    /// </summary>
    [JsonPropertyName("speech_models")]
    public IReadOnlyList<string>? SpeechModels { get; init; }

    /// <summary>For example <c>en_us</c>. Can't be combined with <see cref="LanguageDetection"/>.</summary>
    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; init; }

    [JsonPropertyName("language_detection")]
    public bool? LanguageDetection { get; init; }

    [JsonPropertyName("language_detection_options")]
    public LanguageDetectionOptions? LanguageDetectionOptions { get; init; }

    /// <summary>
    /// <c>medical-v1</c> turns on Medical Mode (English, Spanish, German and French). For other languages it's
    /// skipped with a warning in <c>metadata.warnings</c> and not billed.
    /// </summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    /// <summary>
    /// Words or phrases to bias recognition toward, such as drug and procedure names. Up to 1,000 words in total on
    /// Universal-3.5 Pro. Start with the terms the model actually misses rather than a long static list.
    /// </summary>
    [JsonPropertyName("keyterms_prompt")]
    public IReadOnlyList<string>? KeytermsPrompt { get; init; }

    /// <summary>
    /// A short description of the audio (Universal-3.5 Pro only, up to 1,500 words). Can be combined with
    /// <see cref="KeytermsPrompt"/>. Ignored if the request falls back to Universal-2.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>Speaker diarization. Requires <see cref="Punctuate"/> to stay on.</summary>
    [JsonPropertyName("speaker_labels")]
    public bool? SpeakerLabels { get; init; }

    /// <summary>Exact speaker count. Use either this or <see cref="SpeakerOptions"/>, not both.</summary>
    [JsonPropertyName("speakers_expected")]
    public int? SpeakersExpected { get; init; }

    [JsonPropertyName("speaker_options")]
    public SpeakerOptions? SpeakerOptions { get; init; }

    [JsonPropertyName("multichannel")]
    public bool? Multichannel { get; init; }

    [JsonPropertyName("punctuate")]
    public bool? Punctuate { get; init; }

    [JsonPropertyName("format_text")]
    public bool? FormatText { get; init; }

    [JsonPropertyName("disfluencies")]
    public bool? Disfluencies { get; init; }

    [JsonPropertyName("entity_detection")]
    public bool? EntityDetection { get; init; }

    /// <summary>Requires <see cref="RedactPiiPolicies"/> and <see cref="FormatText"/> left on.</summary>
    [JsonPropertyName("redact_pii")]
    public bool? RedactPii { get; init; }

    /// <summary>Must be listed explicitly when <see cref="RedactPii"/> is on, for example <c>["person_name", "phone_number"]</c>.</summary>
    [JsonPropertyName("redact_pii_policies")]
    public IReadOnlyList<string>? RedactPiiPolicies { get; init; }

    /// <summary><c>hash</c> (default) or <c>entity_name</c>.</summary>
    [JsonPropertyName("redact_pii_sub")]
    public string? RedactPiiSub { get; init; }

    /// <summary>
    /// Speech Understanding tasks to run as part of the job, such as Speaker Identification. Requires
    /// <see cref="SpeakerLabels"/> (or <see cref="Multichannel"/>).
    /// </summary>
    [JsonPropertyName("speech_understanding")]
    public SpeechUnderstandingRequest? SpeechUnderstanding { get; init; }

    /// <summary>Any request parameter this type doesn't model yet. Merged into the top level of the JSON body.</summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? AdditionalProperties { get; init; }
}

public sealed class SpeakerOptions
{
    [JsonPropertyName("min_speakers_expected")]
    public int? MinSpeakersExpected { get; init; }

    [JsonPropertyName("max_speakers_expected")]
    public int? MaxSpeakersExpected { get; init; }

    /// <summary>Adds <c>speaker_confidence</c> to each word and utterance.</summary>
    [JsonPropertyName("include_speaker_confidence")]
    public bool? IncludeSpeakerConfidence { get; init; }
}

public sealed class LanguageDetectionOptions
{
    [JsonPropertyName("expected_languages")]
    public IReadOnlyList<string>? ExpectedLanguages { get; init; }

    [JsonPropertyName("fallback_language")]
    public string? FallbackLanguage { get; init; }

    [JsonPropertyName("code_switching")]
    public bool? CodeSwitching { get; init; }
}

/// <summary>Model strings for <see cref="TranscriptRequest.SpeechModels"/>.</summary>
public static class SpeechModels
{
    public const string Universal35Pro = "universal-3-5-pro";
    public const string Universal2 = "universal-2";

    /// <summary>The recommended setup: Universal-3.5 Pro with Universal-2 as the fallback for other languages.</summary>
    public static IReadOnlyList<string> Recommended { get; } = new[] { Universal35Pro, Universal2 };
}

public static class Domains
{
    /// <summary>Medical Mode.</summary>
    public const string Medical = "medical-v1";
}
