using System.Net.Http.Headers;
using System.Text;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Dictation;

/// <summary>
/// The Dictation API over plain HTTP. One request returns the verbatim transcript (<c>text</c>) and a rewrite
/// (<c>llm_response</c>). Docs: https://www.assemblyai.com/docs/dictation
/// <list type="bullet">
///   <item><description><see cref="TranscribeLiveAsync"/>: upload while the user is still talking (up to 120 seconds, WAV or raw PCM).</description></item>
///   <item><description><see cref="TranscribeRecordingAsync"/>: send a finished recording (up to 20 minutes, many formats).</description></item>
/// </list>
/// </summary>
public sealed class DictationClient
{
    private readonly HttpClient _http;
    private readonly AssemblyAIOptions _options;

    public DictationClient(HttpClient http, AssemblyAIOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>
    /// <c>POST /v1/transcribe/live</c>. Starts the request immediately and streams <paramref name="audio"/> as it is
    /// read, so most of the utterance has been uploaded by the time the user stops talking. Pass a
    /// <see cref="Audio.ProducerStream"/> fed from your microphone, and complete it when recording stops.
    /// The <c>config</c> part is always sent first, which this route requires.
    /// <para>
    /// The route accepts up to 120 seconds of audio, so cap recordings at that length on your side.
    /// </para>
    /// </summary>
    public async Task<DictationResult> TranscribeLiveAsync(
        Stream audio,
        LiveAudioFormat format,
        DictationConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        if (format == LiveAudioFormat.Pcm && (config?.SampleRate is null || config.Channels is null))
        {
            throw new ArgumentException("Raw PCM needs config.SampleRate and config.Channels.", nameof(config));
        }

        var contentType = format == LiveAudioFormat.Wav ? "audio/wav" : "audio/pcm";
        using var request = NewRequest("/v1/transcribe/live", BuildMultipart(audio, contentType, config ?? new DictationConfig()));

        request.Headers.TransferEncodingChunked = true;
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST /v1/transcribe</c> for a recording you already hold in full: WAV, raw PCM, AAC, MP3, M4A, Ogg/Opus,
    /// FLAC or WebM, from 80 ms up to 20 minutes. <paramref name="contentType"/> is the audio's MIME type,
    /// for example <c>audio/wav</c> or <c>audio/mpeg</c>.
    /// </summary>
    public async Task<DictationResult> TranscribeRecordingAsync(
        Stream audio,
        string contentType,
        DictationConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        using var request = NewRequest("/v1/transcribe", BuildMultipart(audio, contentType, config));
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET /warm</c> opens a connection to the Dictation host ahead of the real request. Call it when the user
    /// starts recording, on the same <see cref="HttpClient"/>, so the TLS handshake isn't on the critical path.
    /// </summary>
    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(new Uri(_options.DictationBaseUri, "/warm"), cancellationToken).ConfigureAwait(false);
        await ApiErrors.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal static MultipartContent BuildMultipart(Stream audio, string audioContentType, DictationConfig? config)
    {
        var multipart = new MultipartContent("form-data", "aai-" + Guid.NewGuid().ToString("N"));

        if (config is not null)
        {
            var configPart = new StringContent(AssemblyAIJson.Serialize(config), Encoding.UTF8, "application/json");
            configPart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"config\"" };
            multipart.Add(configPart);
        }

        // The finished-recording route only treats a part as a file when it has a filename, so always send one.
        var audioPart = new StreamContent(audio);
        audioPart.Headers.ContentType = new MediaTypeHeaderValue(audioContentType);
        audioPart.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"audio\"", FileName = "\"audio\"" };
        multipart.Add(audioPart);

        return multipart;
    }

    private HttpRequestMessage NewRequest(string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.DictationBaseUri, path)) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);
        return request;
    }

    private async Task<DictationResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ApiErrors.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<DictationResult>(body);
    }
}
