using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How to shrink an image.</summary>
public sealed class ImageOptimizeOptions
{
    /// <summary>Gets or sets the tag to give the optimized image.</summary>
    public string OutputTag { get; set; }

    /// <summary>Gets the HTTP paths to probe while the container runs.</summary>
    public IList<string> HttpProbePaths { get; } = [];

    /// <summary>Gets or sets how long to keep probing before stopping, in seconds.</summary>
    public int ContinueAfterSeconds { get; set; } = 1;

    /// <summary>Gets or sets the overall timeout, in minutes.</summary>
    public int TimeoutMinutes { get; set; } = 10;
}
