using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Options for <see cref="AnalysisOperations.ScanImageAsync"/>.
/// </summary>
public sealed class TrivyScanOptions
{
    /// <summary>
    /// Gets or sets the Trivy image to run, overriding <see cref="AnalysisOperations.TrivyImage"/> for
    /// this scan only.
    /// </summary>
    public string ToolImage { get; set; }

    /// <summary>
    /// Gets or sets the severities to report, for example <c>["HIGH", "CRITICAL"]</c>. When empty,
    /// Trivy's default (every severity) applies.
    /// </summary>
    public IList<string> Severities { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to report only vulnerabilities that have a fix available.
    /// </summary>
    public bool IgnoreUnfixed { get; set; }

    /// <summary>
    /// Gets or sets the name of the Docker volume holding Trivy's vulnerability database. The volume is
    /// created on demand and reused, so only the first scan pays for the database download.
    /// </summary>
    public string CacheVolumeName { get; set; } = AnalysisOperations.DefaultTrivyCacheVolumeName;

    /// <summary>
    /// Gets or sets how long to wait for the scan, or <see langword="null"/> (the default) to wait
    /// indefinitely. The first scan on a machine downloads the vulnerability database and can take
    /// several minutes, so a short timeout here is usually a mistake.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
