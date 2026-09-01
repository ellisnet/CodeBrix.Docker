using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used for every Docker Engine API payload.
/// </summary>
/// <remarks>
/// The Engine API mixes casing conventions — inspect payloads are PascalCase while stats payloads are
/// snake_case — so matching is case-insensitive and every DTO carries explicit
/// <see cref="JsonPropertyNameAttribute"/> values rather than relying on a global naming policy.
/// </remarks>
internal static class DockerJson
{
    /// <summary>
    /// Gets the shared serializer options.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Serializes a value to a UTF-8 JSON string using <see cref="Options"/>.
    /// </summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Deserializes a JSON string using <see cref="Options"/>.
    /// </summary>
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new DockerDateTimeOffsetConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
