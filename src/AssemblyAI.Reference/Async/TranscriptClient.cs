using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Async;

/// <summary>
/// Pre-recorded transcription over plain HTTP: upload, submit, poll, fetch and delete.
/// Docs: https://www.assemblyai.com/docs/pre-recorded-audio/getting-started/transcribe-an-audio-file
/// </summary>
public sealed class TranscriptClient
{
    private readonly HttpClient _http;
    private readonly AssemblyAIOptions _options;

    public TranscriptClient(HttpClient http, AssemblyAIOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>
    /// <c>POST /v2/upload</c>: streams local audio to AssemblyAI and returns a private <c>upload_url</c> to pass as
    /// <see cref="TranscriptRequest.AudioUrl"/>. The URL only works with API keys from the same project, and it is
    /// removed when you delete the transcript. Uploads aren't retried, because the stream can't be replayed.
    /// </summary>
    public async Task<string> UploadAsync(Stream audio, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Post, "/v2/upload");
        request.Content = new StreamContent(audio);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<UploadResponse>(body).UploadUrl;
    }

    public async Task<string> UploadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.OpenRead(path);
        return await UploadAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST /v2/transcript</c>: queues the job and returns immediately with <c>status: "processing"</c>.</summary>
    public async Task<Transcript> SubmitAsync(TranscriptRequest transcriptRequest, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Post, "/v2/transcript");
        request.Content = new StringContent(AssemblyAIJson.Serialize(transcriptRequest), Encoding.UTF8, "application/json");

        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<Transcript>(body);
    }

    /// <summary><c>GET /v2/transcript/{id}</c>.</summary>
    public async Task<Transcript> GetAsync(string transcriptId, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, $"/v2/transcript/{Uri.EscapeDataString(transcriptId)}");
        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<Transcript>(body);
    }

    /// <summary>
    /// Polls <c>GET /v2/transcript/{id}</c> until the transcript is <c>completed</c> or <c>error</c> and returns it
    /// either way, so check <see cref="Transcript.IsError"/>. The job keeps running on AssemblyAI's side whether or
    /// not you're polling, so store the id when you submit and call this again after a restart to pick it back up.
    /// </summary>
    public async Task<Transcript> WaitForCompletionAsync(string transcriptId, PollingOptions? polling = null, CancellationToken cancellationToken = default)
    {
        polling ??= new PollingOptions();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(polling.Timeout);

        var delay = polling.InitialInterval;
        while (true)
        {
            var transcript = await GetAsync(transcriptId, timeout.Token).ConfigureAwait(false);
            if (transcript.IsFinished)
            {
                return transcript;
            }

            await Task.Delay(polling.WithJitter(delay), timeout.Token).ConfigureAwait(false);
            delay = polling.Next(delay);
        }
    }

    /// <summary>Submits, waits, and throws <see cref="TranscriptFailedException"/> if the job ends in <c>error</c>.</summary>
    public async Task<Transcript> TranscribeAsync(TranscriptRequest transcriptRequest, PollingOptions? polling = null, CancellationToken cancellationToken = default)
    {
        var submitted = await SubmitAsync(transcriptRequest, cancellationToken).ConfigureAwait(false);
        var transcript = await WaitForCompletionAsync(submitted.Id, polling, cancellationToken).ConfigureAwait(false);
        return transcript.IsError ? throw new TranscriptFailedException(transcript) : transcript;
    }

    /// <summary>
    /// <c>DELETE /v2/transcript/{id}</c>: removes the transcript, its outputs and any file uploaded through
    /// <see cref="UploadAsync"/>, right away rather than waiting on your retention TTL. Only allowed once the job is
    /// <c>completed</c> or <c>error</c>. Call it as soon as you've stored the result on your side.
    /// </summary>
    public async Task<Transcript> DeleteAsync(string transcriptId, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Delete, $"/v2/transcript/{Uri.EscapeDataString(transcriptId)}");
        var body = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<Transcript>(body);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.ApiBaseUri, path));
        request.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ApiErrors.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class UploadResponse
    {
        [JsonPropertyName("upload_url")]
        public string UploadUrl { get; init; } = "";
    }
}

/// <summary>
/// Polling cadence. The defaults start at 3 seconds and back off to 15 seconds with ±25% jitter, so many jobs
/// in flight don't poll in lockstep. Polls and submissions share the HTTP rate limit (20,000 requests per five
/// minutes), so widen <see cref="MaxInterval"/> if you run many hundreds of jobs at once.
/// Docs: https://www.assemblyai.com/docs/pre-recorded-audio/rate-limits
/// </summary>
public sealed class PollingOptions
{
    public TimeSpan InitialInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan MaxInterval { get; init; } = TimeSpan.FromSeconds(15);

    public double BackoffMultiplier { get; init; } = 1.5;

    /// <summary>Fraction of the delay to randomize, in both directions.</summary>
    public double Jitter { get; init; } = 0.25;

    /// <summary>Give up waiting after this long. The job keeps running server-side; you can fetch it later.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(1);

    internal TimeSpan Next(TimeSpan current)
    {
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * BackoffMultiplier);
        return next > MaxInterval ? MaxInterval : next;
    }

    internal TimeSpan WithJitter(TimeSpan delay)
    {
        var factor = 1 + ((Random.Shared.NextDouble() * 2) - 1) * Jitter;
        return TimeSpan.FromMilliseconds(Math.Max(0, delay.TotalMilliseconds * factor));
    }
}

/// <summary>The transcript finished with <c>status: "error"</c>. <see cref="Exception.Message"/> is the API's error text.</summary>
public sealed class TranscriptFailedException : Exception
{
    public TranscriptFailedException(Transcript transcript)
        : base(transcript.Error ?? "Transcription failed.")
    {
        Transcript = transcript;
    }

    public Transcript Transcript { get; }
}
