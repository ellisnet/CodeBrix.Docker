namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The outcome of an image optimization run.</summary>
public sealed class ImageOptimizeReport
{
    /// <summary>Gets the image that went in.</summary>
    public string OriginalImage { get; init; }

    /// <summary>Gets the image that came out.</summary>
    public string OptimizedImage { get; init; }

    /// <summary>Gets a value indicating whether the run succeeded.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the original size, in bytes.</summary>
    public long? OriginalSizeBytes { get; init; }

    /// <summary>Gets the optimized size, in bytes.</summary>
    public long? OptimizedSizeBytes { get; init; }

    /// <summary>Gets the size reduction, between zero and one.</summary>
    public double? SizeReduction { get; init; }

    /// <summary>Gets the tool's transcript.</summary>
    public string Output { get; init; } = string.Empty;
}
