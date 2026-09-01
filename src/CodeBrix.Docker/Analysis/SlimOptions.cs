using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Options for <see cref="AnalysisOperations.OptimizeImageAsync"/>.
/// </summary>
/// <remarks>
/// This type belongs to an experimental feature. Slim rebuilds an image from what it observes the
/// container actually touching, so an image whose exercised code paths are not covered while Slim
/// watches it can end up missing files it needs at runtime. Verify optimized images before shipping them.
/// </remarks>
public sealed class SlimOptions
{
    /// <summary>
    /// Gets or sets the Slim image to run, overriding <see cref="AnalysisOperations.SlimImage"/> for this
    /// run only.
    /// </summary>
    public string ToolImage { get; set; }

    /// <summary>
    /// Gets or sets the tag for the optimized image. When omitted, the source reference plus the suffix
    /// <c>.slim</c> is used.
    /// </summary>
    public string OutputTag { get; set; }

    /// <summary>
    /// Gets or sets HTTP paths to probe while the container runs, for example <c>["/", "/health"]</c>.
    /// Each path becomes a <c>--http-probe-cmd</c> argument. When empty, probing is disabled entirely
    /// with <c>--http-probe=false</c>, which is the right choice for images that are not HTTP servers.
    /// </summary>
    public IList<string> HttpProbePaths { get; set; } = [];

    /// <summary>
    /// Gets or sets how many seconds Slim keeps the temporary container running while it observes the
    /// application. Longer values give slow-starting applications time to touch everything they need.
    /// </summary>
    public int ContinueAfterSeconds { get; set; } = 1;

    /// <summary>Gets or sets how long to wait for the optimization to finish.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}
