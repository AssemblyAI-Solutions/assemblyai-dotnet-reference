using System.Net;

namespace AssemblyAI.Reference;

/// <summary>
/// Raised when an AssemblyAI endpoint returns a non-success status code.
/// <see cref="Exception.Message"/> carries the API's own error text when the response body has one.
/// </summary>
public sealed class AssemblyAIApiException : Exception
{
    public AssemblyAIApiException(HttpStatusCode statusCode, string message, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, kept for logging. It never contains audio.</summary>
    public string? ResponseBody { get; }

    /// <summary>True for statuses where retrying the same request later can succeed.</summary>
    public bool IsTransient =>
        StatusCode == HttpStatusCode.TooManyRequests || (int)StatusCode >= 500;
}
