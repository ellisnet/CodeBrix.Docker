using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>What to build, and how.</summary>
public sealed class ImageBuildRequest
{
    /// <summary>Gets or sets the build context directory.</summary>
    public string ContextDirectory { get; set; }

    /// <summary>Gets or sets the Dockerfile path, relative to the context.</summary>
    public string DockerfilePath { get; set; }

    /// <summary>Gets the tags to apply.</summary>
    public IList<string> Tags { get; } = [];

    /// <summary>Gets the build arguments.</summary>
    public IDictionary<string, string> BuildArgs { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the multi-stage target.</summary>
    public string Target { get; set; }

    /// <summary>Gets or sets a value indicating whether base images are re-pulled.</summary>
    public bool Pull { get; set; }

    /// <summary>Gets or sets a value indicating whether the layer cache is bypassed.</summary>
    public bool NoCache { get; set; }

    /// <summary>Gets the labels to apply.</summary>
    public IDictionary<string, string> Labels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
