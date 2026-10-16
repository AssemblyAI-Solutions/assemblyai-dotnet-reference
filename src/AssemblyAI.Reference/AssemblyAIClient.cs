using AssemblyAI.Reference.Async;
using AssemblyAI.Reference.Dictation;
using AssemblyAI.Reference.Http;
using AssemblyAI.Reference.LlmGateway;
using AssemblyAI.Reference.Streaming;

namespace AssemblyAI.Reference;

/// <summary>
/// Entry point that shares one <see cref="HttpClient"/> across every product client. Create one per process and
/// reuse it. If you already use <c>IHttpClientFactory</c>, pass your own client (with <see cref="RetryHandler"/>
/// in its pipeline) to the constructor that takes one.
/// </summary>
public sealed class AssemblyAIClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public AssemblyAIClient(AssemblyAIOptions options)
        : this(options, CreateHttpClient(options), ownsHttpClient: true)
    {
    }

    public AssemblyAIClient(AssemblyAIOptions options, HttpClient httpClient)
        : this(options, httpClient, ownsHttpClient: false)
    {
    }

    private AssemblyAIClient(AssemblyAIOptions options, HttpClient httpClient, bool ownsHttpClient)
    {
        Options = options;
        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        Transcripts = new TranscriptClient(httpClient, options);
        Dictation = new DictationClient(httpClient, options);
        Streaming = new StreamingClient(httpClient, options);
        LlmGateway = new LlmGatewayClient(httpClient, options);
    }

    public AssemblyAIOptions Options { get; }

    public TranscriptClient Transcripts { get; }

    public DictationClient Dictation { get; }

    public StreamingClient Streaming { get; }

    public LlmGatewayClient LlmGateway { get; }

    /// <summary>
    /// A client with connection pooling and <see cref="RetryHandler"/>. The overall timeout is 15 minutes to cover
    /// large uploads and live dictation; pass a <see cref="CancellationToken"/> per call for tighter limits.
    /// The API key is added per request rather than as a default header, so it is never sent to another host.
    /// </summary>
    public static HttpClient CreateHttpClient(AssemblyAIOptions options)
    {
        var sockets = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };

        return new HttpClient(new RetryHandler(options.MaxRetries) { InnerHandler = sockets })
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
