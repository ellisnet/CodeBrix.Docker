using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Turns the JSON report Trivy writes to standard output into a <see cref="TrivyScanResult"/>.
/// </summary>
internal static class TrivyOutputParser
{
    /// <summary>
    /// Parses a Trivy report.
    /// </summary>
    /// <param name="imageReference">The image that was scanned.</param>
    /// <param name="stdout">Trivy's standard output.</param>
    /// <param name="stderr">Trivy's standard error, used only to explain failures.</param>
    /// <param name="exitCode">The tool container's exit code.</param>
    /// <returns>The parsed result.</returns>
    /// <exception cref="DockerException">The output holds no report that can be parsed.</exception>
    public static TrivyScanResult Parse(string imageReference, string stdout, string stderr, long exitCode)
    {
        var json = AnalysisJson.ExtractObject(stdout);
        if (json is null)
        {
            throw new DockerException(
                $"Trivy produced no JSON report for '{imageReference}' (exit code {exitCode}). " +
                $"Output: {AnalysisJson.Describe(stdout, stderr)}");
        }

        TrivyReportWire report;
        try
        {
            report = DockerJson.Deserialize<TrivyReportWire>(json);
        }
        catch (JsonException ex)
        {
            throw new DockerException(
                $"Could not parse Trivy's report for '{imageReference}': {ex.Message}", ex);
        }

        if (report is null)
        {
            throw new DockerException($"Trivy's report for '{imageReference}' was empty.");
        }

        var vulnerabilities = new List<TrivyVulnerability>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in report.Results ?? [])
        {
            foreach (var entry in target.Vulnerabilities ?? [])
            {
                var severity = string.IsNullOrWhiteSpace(entry.Severity)
                    ? "UNKNOWN"
                    : entry.Severity.ToUpperInvariant();

                vulnerabilities.Add(new TrivyVulnerability(
                    entry.VulnerabilityId ?? string.Empty,
                    entry.PkgName ?? string.Empty,
                    entry.InstalledVersion,
                    string.IsNullOrWhiteSpace(entry.FixedVersion) ? null : entry.FixedVersion,
                    severity,
                    entry.Title)
                {
                    Target = target.Target,
                });

                counts[severity] = counts.TryGetValue(severity, out var count) ? count + 1 : 1;
            }
        }

        return new TrivyScanResult
        {
            ImageReference = imageReference,
            ArtifactName = report.ArtifactName,
            Vulnerabilities = vulnerabilities,
            CountBySeverity = counts,
            ExitCode = exitCode,
        };
    }
}

/// <summary>The top level of Trivy's JSON report.</summary>
internal sealed class TrivyReportWire
{
    [JsonPropertyName("SchemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("ArtifactName")]
    public string ArtifactName { get; init; }

    [JsonPropertyName("Results")]
    public IReadOnlyList<TrivyTargetWire> Results { get; init; }
}

/// <summary>One scan target inside a Trivy report — the operating-system packages or one lock file.</summary>
internal sealed class TrivyTargetWire
{
    [JsonPropertyName("Target")]
    public string Target { get; init; }

    [JsonPropertyName("Class")]
    public string Class { get; init; }

    [JsonPropertyName("Type")]
    public string Type { get; init; }

    [JsonPropertyName("Vulnerabilities")]
    public IReadOnlyList<TrivyVulnerabilityWire> Vulnerabilities { get; init; }
}

/// <summary>One vulnerability entry inside a Trivy report.</summary>
internal sealed class TrivyVulnerabilityWire
{
    [JsonPropertyName("VulnerabilityID")]
    public string VulnerabilityId { get; init; }

    [JsonPropertyName("PkgName")]
    public string PkgName { get; init; }

    [JsonPropertyName("InstalledVersion")]
    public string InstalledVersion { get; init; }

    [JsonPropertyName("FixedVersion")]
    public string FixedVersion { get; init; }

    [JsonPropertyName("Severity")]
    public string Severity { get; init; }

    [JsonPropertyName("Title")]
    public string Title { get; init; }
}
