using System.Globalization;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Streaming;

/// <summary>
/// Opens Streaming sessions and mints temporary tokens.
/// Docs: https://www.assemblyai.com/docs/streaming/getting-started/transcribe-streaming-audio
/// </summary>
public sealed class StreamingClient
{
    private readonly HttpClient _http;
    private readonly AssemblyAIOptions _options;

    public StreamingClient(HttpClient http, AssemblyAIOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>Connects with your API key (server side) and waits for <c>Begin</c>.</summary>
    public Task<StreamingSession> ConnectAsync(StreamingParameters parameters, CancellationToken cancellationToken = default) =>
        StreamingSession.ConnectAsync(BuildUri(parameters, token: null), _options.ApiKey, cancellationToken);

    /// <summary>
    /// Connects with a temporary token instead of the API key. Use this from a browser or a clinician's device:
    /// mint the token on your server with <see cref="CreateTemporaryTokenAsync"/> and hand only the token out.
    /// </summary>
    public Task<StreamingSession> ConnectWithTokenAsync(string token, StreamingParameters parameters, CancellationToken cancellationToken = default) =>
        StreamingSession.ConnectAsync(BuildUri(parameters, token), apiKey: null, cancellationToken);

    /// <summary>
    /// <c>GET /v3/token</c>. <paramref name="expiresInSeconds"/> (1-600) is how long the token can be used to start
    /// sessions; <paramref name="maxSessionDurationSeconds"/> (60-10800) caps each session started with it.
    /// </summary>
    public async Task<string> CreateTemporaryTokenAsync(
        int expiresInSeconds,
        int? maxSessionDurationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"expires_in_seconds={expiresInSeconds.ToString(CultureInfo.InvariantCulture)}";
        if (maxSessionDurationSeconds is { } max)
        {
            query += $"&max_session_duration_seconds={max.ToString(CultureInfo.InvariantCulture)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.StreamingTokenBaseUri, $"/v3/token?{query}"));
        request.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await ApiErrors.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return AssemblyAIJson.Deserialize<TokenResponse>(body).Token;
    }

    internal Uri BuildUri(StreamingParameters parameters, string? token)
    {
        var query = parameters.ToQueryString();
        if (token is not null)
        {
            query += "&token=" + Uri.EscapeDataString(token);
        }

        return new Uri(_options.StreamingBaseUri, "/v3/ws?" + query);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = "";
    }
}
