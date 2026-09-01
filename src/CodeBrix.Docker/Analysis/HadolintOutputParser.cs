using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Turns the JSON array Hadolint writes to standard output into a <see cref="HadolintResult"/>.
/// </summary>
internal static class HadolintOutputParser
{
    /// <summary>
    /// Parses a Hadolint report.
    /// </summary>
    /// <param name="dockerfilePath">The Dockerfile that was linted.</param>
    /// <param name="stdout">Hadolint's standard output.</param>
    /// <param name="stderr">Hadolint's standard error, used only to explain failures.</param>
    /// <param name="exitCode">The tool container's exit code.</param>
    /// <returns>The parsed result.</returns>
    /// <exception cref="DockerException">The output holds no report that can be parsed.</exception>
    public static HadolintResult Parse(string dockerfilePath, string stdout, string stderr, long exitCode)
    {
        var json = AnalysisJson.ExtractArray(stdout);
        if (json is null)
        {
            throw new DockerException(
                $"Hadolint produced no JSON report for '{dockerfilePath}' (exit code {exitCode}). " +
                $"Output: {AnalysisJson.Describe(stdout, stderr)}");
        }

        IReadOnlyList<HadolintFindingWire> entries;
        try
        {
            entries = DockerJson.Deserialize<List<HadolintFindingWire>>(json);
        }
        catch (JsonException ex)
        {
            throw new DockerException($"Could not parse Hadolint's report for '{dockerfilePath}': {ex.Message}", ex);
        }

        var findings = new List<HadolintFinding>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries ?? [])
        {
            var level = string.IsNullOrWhiteSpace(entry.Level) ? "info" : entry.Level.ToLowerInvariant();

            findings.Add(new HadolintFinding(
                entry.Code ?? string.Empty,
                level,
                entry.Line,
                entry.Message ?? string.Empty)
            {
                Column = entry.Column,
            });

            counts[level] = counts.TryGetValue(level, out var count) ? count + 1 : 1;
        }

        return new HadolintResult
        {
            DockerfilePath = dockerfilePath,
            Findings = findings,
            CountByLevel = counts,
            ExitCode = exitCode,
        };
    }
}

/// <summary>One entry of Hadolint's JSON output.</summary>
internal sealed class HadolintFindingWire
{
    [JsonPropertyName("file")]
    public string File { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("column")]
    public int Column { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; }
}
