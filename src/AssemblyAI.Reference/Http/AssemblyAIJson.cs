using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssemblyAI.Reference.Http;

/// <summary>
/// One shared set of serializer options. Every model names its JSON fields explicitly with
/// <see cref="JsonPropertyNameAttribute"/> so the wire names are easy to grep against the API docs.
/// Null properties are left out of requests, so you only send the parameters you set.
/// </summary>
public static class AssemblyAIJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"The response body did not contain a {typeof(T).Name}.");
}
