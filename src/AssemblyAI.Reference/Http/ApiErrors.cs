using System.Net;
using System.Text.Json;

namespace AssemblyAI.Reference.Http;

internal static class ApiErrors
{
    /// <summary>
    /// Throws <see cref="AssemblyAIApiException"/> for a non-success response, using the error text
    /// from the body. AssemblyAI products use a few error shapes: <c>{"error": "..."}</c> on the
    /// pre-recorded API, problem details (<c>title</c>/<c>detail</c>) on Dictation, and
    /// <c>{"error": {"message": "..."}}</c> on OpenAI-compatible LLM Gateway routes.
    /// </summary>
    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        throw new AssemblyAIApiException(
            response.StatusCode,
            ExtractMessage(body) ?? $"AssemblyAI returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            body);
    }

    internal static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nested)
                    && nested.ValueKind == JsonValueKind.String)
                {
                    return nested.GetString();
                }
            }

            var detail = root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            var title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (detail is not null || title is not null)
            {
                return title is not null && detail is not null ? $"{title}: {detail}" : detail ?? title;
            }

            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                return m.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page from a proxy); fall back to the status line.
        }

        return null;
    }

    internal static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
