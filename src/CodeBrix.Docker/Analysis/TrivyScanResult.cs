using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a Trivy vulnerability scan of a container image.
/// </summary>
public sealed class TrivyScanResult
{
    /// <summary>Gets the image reference that was scanned.</summary>
    public required string ImageReference { get; init; }

    /// <summary>Gets the artifact name Trivy reported, which is normally the image reference.</summary>
    public string ArtifactName { get; init; }

    /// <summary>Gets every vulnerability found, across all of Trivy's scan targets.</summary>
    public required IReadOnlyList<TrivyVulnerability> Vulnerabilities { get; init; }

    /// <summary>
    /// Gets the number of vulnerabilities per severity, keyed by Trivy's uppercase severity names
    /// (<c>UNKNOWN</c>, <c>LOW</c>, <c>MEDIUM</c>, <c>HIGH</c>, <c>CRITICAL</c>). Severities with no
    /// findings are absent.
    /// </summary>
    public required IReadOnlyDictionary<string, int> CountBySeverity { get; init; }

    /// <summary>Gets the total number of vulnerabilities found.</summary>
    public int Total => Vulnerabilities.Count;

    /// <summary>
    /// Gets the exit code of the Trivy container. A non-zero code is not by itself a failure — it is how
    /// Trivy reports findings when <c>--exit-code</c> is in play.
    /// </summary>
    public long ExitCode { get; init; }

    /// <summary>Gets the number of vulnerabilities of the given severity, or zero when there are none.</summary>
    /// <param name="severity">The severity name; matching is case-insensitive.</param>
    /// <returns>The count.</returns>
    public int CountOf(string severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        return CountBySeverity.TryGetValue(severity.ToUpperInvariant(), out var count) ? count : 0;
    }
}
