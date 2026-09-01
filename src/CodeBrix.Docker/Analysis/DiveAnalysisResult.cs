using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a Dive layer-efficiency analysis of a container image.
/// </summary>
public sealed class DiveAnalysisResult
{
    /// <summary>Gets the image reference that was analyzed.</summary>
    public required string ImageReference { get; init; }

    /// <summary>
    /// Gets the efficiency score between 0 and 1, where 1 means no bytes are wasted. Anything below
    /// roughly 0.9 usually means files are being written and then overwritten or deleted in a later
    /// layer, which keeps both copies in the image.
    /// </summary>
    public required double EfficiencyScore { get; init; }

    /// <summary>
    /// Gets the number of bytes duplicated across layers — space that a better-ordered Dockerfile would
    /// not spend.
    /// </summary>
    public required long WastedBytes { get; init; }

    /// <summary>Gets the total size of the image in bytes as Dive measured it.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Gets the layers, in build order.</summary>
    public required IReadOnlyList<DiveLayerInfo> Layers { get; init; }

    /// <summary>
    /// Gets the exit code of the Dive container. Dive's continuous-integration mode returns a non-zero
    /// code when the image fails its built-in efficiency rules, which is a finding rather than an error.
    /// </summary>
    public long ExitCode { get; init; }

    /// <summary>Gets the wasted bytes as a fraction of the total image size, or zero for an empty image.</summary>
    public double WastedPercent => TotalSizeBytes > 0 ? (double)WastedBytes / TotalSizeBytes : 0d;
}
