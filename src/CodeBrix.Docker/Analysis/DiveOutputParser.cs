using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Turns the JSON export Dive writes inside its container into a <see cref="DiveAnalysisResult"/>.
/// </summary>
internal static class DiveOutputParser
{
    /// <summary>
    /// Parses a Dive export.
    /// </summary>
    /// <param name="imageReference">The image that was analyzed.</param>
    /// <param name="json">The contents of Dive's JSON export file.</param>
    /// <param name="exitCode">The tool container's exit code.</param>
    /// <returns>The parsed result.</returns>
    /// <exception cref="DockerException">The export holds no analysis that can be parsed.</exception>
    public static DiveAnalysisResult Parse(string imageReference, string json, long exitCode)
    {
        var payload = AnalysisJson.ExtractObject(json)
                      ?? throw new DockerException(
                          $"Dive produced no JSON analysis for '{imageReference}' (exit code {exitCode}).");

        DiveReportWire report;
        try
        {
            report = DockerJson.Deserialize<DiveReportWire>(payload);
        }
        catch (JsonException ex)
        {
            throw new DockerException($"Could not parse Dive's analysis of '{imageReference}': {ex.Message}", ex);
        }

        if (report?.Image is null)
        {
            throw new DockerException($"Dive's analysis of '{imageReference}' contained no image summary.");
        }

        var layers = new List<DiveLayerInfo>();
        foreach (var layer in report.Layer ?? [])
        {
            layers.Add(new DiveLayerInfo(layer.Index, layer.SizeBytes, layer.Command ?? string.Empty)
            {
                Digest = layer.DigestId ?? layer.Id,
            });
        }

        return new DiveAnalysisResult
        {
            ImageReference = imageReference,
            EfficiencyScore = report.Image.EfficiencyScore,
            WastedBytes = report.Image.InefficientBytes,
            TotalSizeBytes = report.Image.SizeBytes,
            Layers = layers,
            ExitCode = exitCode,
        };
    }
}

/// <summary>The top level of Dive's JSON export.</summary>
internal sealed class DiveReportWire
{
    [JsonPropertyName("layer")]
    public IReadOnlyList<DiveLayerWire> Layer { get; init; }

    [JsonPropertyName("image")]
    public DiveImageWire Image { get; init; }
}

/// <summary>The image summary inside Dive's JSON export.</summary>
internal sealed class DiveImageWire
{
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("inefficientBytes")]
    public long InefficientBytes { get; init; }

    [JsonPropertyName("efficiencyScore")]
    public double EfficiencyScore { get; init; }
}

/// <summary>One layer inside Dive's JSON export.</summary>
internal sealed class DiveLayerWire
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("digestId")]
    public string DigestId { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; }
}
