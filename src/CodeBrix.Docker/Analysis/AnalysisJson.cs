using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Helpers for pulling a JSON document out of a tool container's console output, which can carry
/// banners, warnings or progress text around the payload.
/// </summary>
internal static class AnalysisJson
{
    /// <summary>
    /// Returns the outermost JSON object in <paramref name="text"/>, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    public static string ExtractObject(string text) => Extract(text, '{', '}');

    /// <summary>
    /// Returns the outermost JSON array in <paramref name="text"/>, or <see langword="null"/> when
    /// there is none.
    /// </summary>
    public static string ExtractArray(string text) => Extract(text, '[', ']');

    /// <summary>
    /// Builds a short, single-line description of a tool's output for an error message.
    /// </summary>
    public static string Describe(string stdout, string stderr)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(no output)";
        }

        var flattened = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= 2000 ? flattened : flattened[..2000] + "...";
    }

    private static string Extract(string text, char open, char close)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf(open);
        var end = text.LastIndexOf(close);
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}

/// <summary>One progress line of the JSON-lines stream returned by <c>POST /images/create</c>.</summary>
internal sealed class AnalysisPullProgress
{
    [JsonPropertyName("status")]
    public string Status { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; }
}

/// <summary>Request body for <c>POST /volumes/create</c> as the analysis operations use it.</summary>
internal sealed class AnalysisVolumeCreateRequest
{
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    [JsonPropertyName("Labels")]
    public IDictionary<string, string> Labels { get; init; }
}

/// <summary>The few fields of <c>GET /images/{name}/json</c> the analysis operations need.</summary>
internal sealed class AnalysisImageInfo
{
    [JsonPropertyName("Id")]
    public string Id { get; init; }

    [JsonPropertyName("Size")]
    public long Size { get; init; }
}
