using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AssemblyAI.Reference.Streaming;

/// <summary>
/// Connection parameters for the Streaming API (<c>/v3/ws</c>), sent as the WebSocket URL's query string.
/// Docs: https://www.assemblyai.com/docs/streaming/select-the-speech-model
/// <para>
/// Unknown parameters are ignored by the server rather than rejected, so check <see cref="BeginMessage.Configuration"/>
/// if you need to confirm a setting took effect.
/// </para>
/// </summary>
public sealed class StreamingParameters
{
    /// <summary>Always set the model explicitly. <c>universal-3-6-pro</c> is the current recommended model.</summary>
    public string SpeechModel { get; init; } = StreamingSpeechModels.Universal36Pro;

    /// <summary>Sample rate of the audio you send. Must match your PCM (16000 is the default).</summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary><c>pcm_s16le</c> (default), <c>pcm_mulaw</c>, <c>opus</c>, <c>ogg_opus</c> or <c>aac</c>.</summary>
    public string Encoding { get; init; } = "pcm_s16le";

    /// <summary><c>medical-v1</c> turns on Medical Mode. It corrects final turns only and is billed as an add-on.</summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Label final turns and words by speaker. This also switches turn detection to values tuned for diarization, so
    /// turns end when the speaker changes. Leave <see cref="MinTurnSilence"/> and <see cref="MaxTurnSilence"/> unset
    /// with it, since values you set replace those.
    /// </summary>
    public bool? SpeakerLabels { get; init; }

    /// <summary>Hard cap on speaker labels (1-10). Leave a little headroom above the speakers you expect.</summary>
    public int? MaxSpeakers { get; init; }

    /// <summary>
    /// Ask for <c>SpeakerRevision</c> messages during the session, not only at the end. 300000 (5 minutes) is the
    /// recommended value; non-zero values are clamped to between 2 and 5 minutes.
    /// </summary>
    public int? SpeakerLabelsRevisionIntervalMs { get; init; }

    /// <summary>Up to 100 terms to bias recognition toward (drug names, provider names).</summary>
    public IReadOnlyList<string>? KeytermsPrompt { get; init; }

    /// <summary>Context describing the audio, up to 1,750 characters.</summary>
    public string? Prompt { get; init; }

    /// <summary>Restrict the session to these languages, for example <c>["en"]</c>.</summary>
    public IReadOnlyList<string>? LanguageCodes { get; init; }

    /// <summary><c>min_latency</c>, <c>balanced</c> (default) or <c>max_accuracy</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>Silence (ms) before the server checks whether the turn has ended. One clinician dictating: 800.</summary>
    public int? MinTurnSilence { get; init; }

    /// <summary>Silence (ms) after which the turn is ended regardless. One clinician dictating: 3600.</summary>
    public int? MaxTurnSilence { get; init; }

    /// <summary>Close the session after this many seconds without any message from you (5-3600).</summary>
    public int? InactivityTimeout { get; init; }

    /// <summary><c>near-field</c> (headsets, phones) or <c>far-field</c> (room microphones). Billed as an add-on.</summary>
    public string? VoiceFocus { get; init; }

    public bool? RedactPii { get; init; }

    public IReadOnlyList<string>? RedactPiiPolicies { get; init; }

    /// <summary>Any parameter this type doesn't cover yet. Values are sent as-is.</summary>
    public IReadOnlyDictionary<string, string>? AdditionalParameters { get; init; }

    /// <summary>
    /// Builds the query string. List parameters are sent as a single JSON array string
    /// (<c>keyterms_prompt=["a","b"]</c>, URL-encoded): the server keeps only the first value of a repeated key,
    /// so <c>keyterms_prompt=a&amp;keyterms_prompt=b</c> would silently drop every term after the first.
    /// </summary>
    public string ToQueryString()
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("speech_model", SpeechModel),
            new("sample_rate", SampleRate.ToString(CultureInfo.InvariantCulture)),
            new("encoding", Encoding),
        };

        void Add(string key, object? value)
        {
            switch (value)
            {
                case null:
                    return;
                case bool b:
                    pairs.Add(new(key, b ? "true" : "false"));
                    break;
                case int i:
                    pairs.Add(new(key, i.ToString(CultureInfo.InvariantCulture)));
                    break;
                case IReadOnlyList<string> list:
                    pairs.Add(new(key, JsonSerializer.Serialize(list)));
                    break;
                default:
                    pairs.Add(new(key, Convert.ToString(value, CultureInfo.InvariantCulture)!));
                    break;
            }
        }

        Add("domain", Domain);
        Add("speaker_labels", SpeakerLabels);
        Add("max_speakers", MaxSpeakers);
        Add("speaker_labels_revision_interval_ms", SpeakerLabelsRevisionIntervalMs);
        Add("keyterms_prompt", KeytermsPrompt);
        Add("prompt", Prompt);
        Add("language_codes", LanguageCodes);
        Add("mode", Mode);
        Add("min_turn_silence", MinTurnSilence);
        Add("max_turn_silence", MaxTurnSilence);
        Add("inactivity_timeout", InactivityTimeout);
        Add("voice_focus", VoiceFocus);
        Add("redact_pii", RedactPii);
        Add("redact_pii_policies", RedactPiiPolicies);

        if (AdditionalParameters is not null)
        {
            foreach (var (key, value) in AdditionalParameters)
            {
                pairs.Add(new(key, value));
            }
        }

        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}

public static class StreamingSpeechModels
{
    public const string Universal36Pro = "universal-3-6-pro";
    public const string Universal35Pro = "universal-3-5-pro";
    public const string UniversalStreamingEnglish = "universal-streaming-english";
    public const string UniversalStreamingMultilingual = "universal-streaming-multilingual";
}
