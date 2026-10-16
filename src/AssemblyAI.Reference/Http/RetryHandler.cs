using System.Net;
using System.Net.Http.Headers;

namespace AssemblyAI.Reference.Http;

/// <summary>
/// Retries requests that are safe to send again:
/// <list type="bullet">
///   <item><description><b>429 Too Many Requests</b> on any method, because the request was rejected before it was processed.</description></item>
///   <item><description><b>5xx, 408 and network failures</b> only on idempotent methods (GET, HEAD, DELETE), because a retried POST could create a second transcript and bill twice.</description></item>
///   <item><description><b>403 on idempotent methods</b>, because the pre-recorded API answers 403 (not 429) when you exceed its HTTP rate limit, which is what a busy polling loop would hit.</description></item>
/// </list>
/// Requests whose body is a stream (file uploads, live dictation audio) are never retried, since the stream can't be replayed.
/// <c>Retry-After</c> is honored; otherwise the delay is exponential with jitter.
/// </summary>
public sealed class RetryHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public RetryHandler(int maxRetries = 4, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _maxRetries && IsIdempotent(request.Method) && IsReplayable(request))
            {
                await Task.Delay(Backoff(attempt, retryAfter: null), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (attempt >= _maxRetries || !ShouldRetry(request, response.StatusCode))
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter;
            response.Dispose();
            await Task.Delay(Backoff(attempt, retryAfter), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool ShouldRetry(HttpRequestMessage request, HttpStatusCode status)
    {
        if (!IsReplayable(request))
        {
            return false;
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // 403 is how the pre-recorded API signals "over the HTTP rate limit". A POST can also get 403 for a real
        // permission problem (an upload_url from another project), so only idempotent requests retry it.
        var transient = status == HttpStatusCode.RequestTimeout || status == HttpStatusCode.Forbidden || (int)status >= 500;
        return transient && IsIdempotent(request.Method);
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Delete;

    // Buffered bodies (StringContent and ByteArrayContent) can be sent again; streams can't.
    private static bool IsReplayable(HttpRequestMessage request) =>
        request.Content is null or ByteArrayContent;

    private TimeSpan Backoff(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return Clamp(delta);
        }

        if (retryAfter?.Date is { } date)
        {
            return Clamp(date - DateTimeOffset.UtcNow);
        }

        var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * _baseDelay.TotalMilliseconds;
        return Clamp(TimeSpan.FromMilliseconds(exponential + jitter));
    }

    private TimeSpan Clamp(TimeSpan delay) =>
        delay < TimeSpan.Zero ? TimeSpan.Zero : delay > _maxDelay ? _maxDelay : delay;
}
