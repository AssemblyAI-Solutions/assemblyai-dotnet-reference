using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssemblyAI.Reference.Dictation;

/// <summary>
/// The JSON <c>config</c> part of a Dictation request. Every field except <see cref="LlmInstruction"/> steers
/// transcription; <see cref="LlmInstruction"/> replaces the default cleanup rewrite.
/// Docs: https://www.assemblyai.com/docs/dictation#config-parameters
/// <para>
/// Medical Mode (<c>domain</c>) isn't a Dictation option, and unknown fields are rejected with a 400. For clinical
/// vocabulary, use <see cref="SttPrompt"/> and <see cref="KeytermsPrompt"/>.
/// </para>
/// </summary>
public sealed class DictationConfig
{
    /// <summary>Required for raw PCM (one of 8000, 16000, 22050, 24000, 32000, 44100, 48000). Ignored for WAV.</summary>
    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    /// <summary>Required for raw PCM (1 or 2).</summary>
    [JsonPropertyName("channels")]
    public int? Channels { get; init; }

    /// <summary>For example <c>["en"]</c>. When omitted, no language steering is applied.</summary>
    [JsonPropertyName("language_codes")]
    public IReadOnlyList<string>? LanguageCodes { get; init; }

    /// <summary>
    /// Describes the situation rather than instructing the model, for example
    /// "A doctor dictating a patient visit note." Up to 6,000 characters.
    /// </summary>
    [JsonPropertyName("stt_prompt")]
    public string? SttPrompt { get; init; }

    /// <summary>Up to 100 terms (8,000 characters in total), such as drug and procedure names.</summary>
    [JsonPropertyName("keyterms_prompt")]
    public IReadOnlyList<string>? KeytermsPrompt { get; init; }

    /// <summary>
    /// Plain-English description of the rewrite you want, for example
    /// "Remove filler words and rewrite as a concise clinical chart note." Describe only the transformation.
    /// Leave it unset to keep the default cleanup (fillers removed, self-corrections resolved, punctuation and
    /// capitalization applied, wording otherwise kept as spoken).
    /// </summary>
    [JsonPropertyName("llm_instruction")]
    public string? LlmInstruction { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? AdditionalProperties { get; init; }
}

/// <summary>Response from both Dictation routes.</summary>
public sealed class DictationResult
{
    /// <summary>The transcript exactly as spoken.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    /// <summary>
    /// The rewrite (default cleanup or your <c>llm_instruction</c>). Null when the rewrite was skipped or failed;
    /// check <see cref="LlmError"/>.
    /// </summary>
    [JsonPropertyName("llm_response")]
    public string? LlmResponse { get; init; }

    /// <summary>Why the rewrite didn't run, for example <c>timeout</c>. Treat it as an open-ended string.</summary>
    [JsonPropertyName("llm_error")]
    public string? LlmError { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<DictationWord>? Words { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("audio_duration_ms")]
    public long? AudioDurationMs { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("request_time_ms")]
    public double? RequestTimeMs { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }

    /// <summary>The text to put in front of the user: the rewrite, falling back to the verbatim transcript.</summary>
    [JsonIgnore]
    public string FinalText => string.IsNullOrEmpty(LlmResponse) ? Text : LlmResponse;
}

public sealed class DictationWord
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

/// <summary>Audio formats the live route accepts. It decodes as bytes arrive, so compressed formats aren't allowed there.</summary>
public enum LiveAudioFormat
{
    /// <summary>WAV with a header. Sample rate and channels come from the header.</summary>
    Wav,

    /// <summary>Raw 16-bit little-endian PCM. Set <see cref="DictationConfig.SampleRate"/> and <see cref="DictationConfig.Channels"/>.</summary>
    Pcm,
}
