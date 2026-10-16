using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace AssemblyAI.Reference.Webhooks;

/// <summary>
/// The body AssemblyAI POSTs to your <c>webhook_url</c> when a transcript finishes. It carries no transcript text,
/// so fetch the transcript with <c>GET /v2/transcript/{id}</c> after acknowledging.
/// Docs: https://www.assemblyai.com/docs/pre-recorded-audio/webhooks
/// <para>
/// Respond with a 2xx within 10 seconds and do the fetch afterwards; slower responses count as failures and are
/// retried. Requests come from 44.238.19.20 (US) and 54.220.25.36 (EU) if you need an allowlist.
/// </para>
/// </summary>
public sealed class WebhookNotification
{
    [JsonPropertyName("transcript_id")]
    public string TranscriptId { get; init; } = "";

    /// <summary><c>completed</c> or <c>error</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
}

public static class WebhookAuthentication
{
    /// <summary>
    /// Checks the header you configured with <c>webhook_auth_header_name</c>/<c>webhook_auth_header_value</c>,
    /// in constant time so the comparison doesn't leak the secret through timing.
    /// </summary>
    public static bool IsValid(string? receivedValue, string expectedValue)
    {
        if (receivedValue is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(receivedValue),
            Encoding.UTF8.GetBytes(expectedValue));
    }
}
