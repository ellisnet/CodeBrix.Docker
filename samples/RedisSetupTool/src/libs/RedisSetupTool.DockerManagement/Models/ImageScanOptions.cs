using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How to scan an image for vulnerabilities.</summary>
public sealed class ImageScanOptions
{
    /// <summary>Gets the severities to report; empty means all of them.</summary>
    public IList<string> Severities { get; } = [];

    /// <summary>Gets or sets a value indicating whether findings with no fix are dropped.</summary>
    public bool IgnoreUnfixed { get; set; }

    /// <summary>Gets or sets how long to allow the scan, in seconds.</summary>
    public int? TimeoutSeconds { get; set; }
}
