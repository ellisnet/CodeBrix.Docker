using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How much of an image is wasted bytes.</summary>
public sealed class ImageEfficiencyReport
{
    /// <summary>Gets the image that was analysed.</summary>
    public string ImageReference { get; init; }

    /// <summary>Gets the efficiency score, between zero and one.</summary>
    public double EfficiencyScore { get; init; }

    /// <summary>Gets the wasted bytes.</summary>
    public long WastedBytes { get; init; }

    /// <summary>Gets the wasted share of the image, between zero and one.</summary>
    public double WastedPercent { get; init; }

    /// <summary>Gets the image size, in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Gets the per-layer breakdown; never null.</summary>
    public IReadOnlyList<EfficiencyLayerInfo> Layers { get; init; } = [];
}
