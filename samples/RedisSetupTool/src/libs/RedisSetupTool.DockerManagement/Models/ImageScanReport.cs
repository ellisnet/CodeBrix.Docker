using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The outcome of an image vulnerability scan.</summary>
public sealed class ImageScanReport
{
    /// <summary>Gets the image that was scanned.</summary>
    public string ImageReference { get; init; }

    /// <summary>Gets the number of findings.</summary>
    public int Total { get; init; }

    /// <summary>Gets the finding count per severity; never null.</summary>
    public IReadOnlyDictionary<string, int> CountBySeverity { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Gets the findings; never null.</summary>
    public IReadOnlyList<VulnerabilityInfo> Vulnerabilities { get; init; } = [];
}
