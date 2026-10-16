namespace AssemblyAI.Reference;

/// <summary>Which AssemblyAI data zone to use. EU endpoints need an API key created in the EU dashboard.</summary>
public enum AssemblyAIRegion
{
    US,
    EU,
}

/// <summary>
/// Settings shared by every client. Each product has its own host, and every host here is pinned to the
/// chosen region so audio and transcripts stay in that data zone.
/// Docs: https://www.assemblyai.com/docs/pre-recorded-audio/select-the-region
/// </summary>
public sealed class AssemblyAIOptions
{
    /// <summary>Sent as the raw <c>Authorization</c> header value (no <c>Bearer</c> prefix).</summary>
    public required string ApiKey { get; init; }

    public AssemblyAIRegion Region { get; init; } = AssemblyAIRegion.US;

    /// <summary>How many times <see cref="Http.RetryHandler"/> retries a request that is safe to retry.</summary>
    public int MaxRetries { get; init; } = 4;

    /// <summary>Pre-recorded API: uploads, transcripts, deletes.</summary>
    public Uri ApiBaseUri => Region == AssemblyAIRegion.EU
        ? new Uri("https://api.eu.assemblyai.com")
        : new Uri("https://api.assemblyai.com");

    /// <summary>Streaming WebSocket host.</summary>
    public Uri StreamingBaseUri => Region == AssemblyAIRegion.EU
        ? new Uri("wss://streaming.eu.assemblyai.com")
        : new Uri("wss://streaming.us.assemblyai.com");

    /// <summary>Host for minting temporary streaming tokens.</summary>
    public Uri StreamingTokenBaseUri => Region == AssemblyAIRegion.EU
        ? new Uri("https://streaming.eu.assemblyai.com")
        : new Uri("https://streaming.assemblyai.com");

    /// <summary>Dictation API host.</summary>
    public Uri DictationBaseUri => Region == AssemblyAIRegion.EU
        ? new Uri("https://dictation.eu.assemblyai.com")
        : new Uri("https://dictation.us.assemblyai.com");

    /// <summary>LLM Gateway host (chat completions and Speech Understanding).</summary>
    public Uri LlmGatewayBaseUri => Region == AssemblyAIRegion.EU
        ? new Uri("https://llm-gateway.eu.assemblyai.com")
        : new Uri("https://llm-gateway.assemblyai.com");

    /// <summary>
    /// Reads <c>ASSEMBLYAI_API_KEY</c> and, optionally, <c>ASSEMBLYAI_REGION</c> (<c>US</c> or <c>EU</c>).
    /// Keep the key in a secret store in production rather than in source or config files.
    /// </summary>
    public static AssemblyAIOptions FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("ASSEMBLYAI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Set the ASSEMBLYAI_API_KEY environment variable.");
        }

        var region = Environment.GetEnvironmentVariable("ASSEMBLYAI_REGION");
        return new AssemblyAIOptions
        {
            ApiKey = key.Trim(),
            Region = string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase) ? AssemblyAIRegion.EU : AssemblyAIRegion.US,
        };
    }
}
