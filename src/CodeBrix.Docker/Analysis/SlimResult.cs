namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a Slim optimization run.
/// </summary>
/// <remarks>
/// This type belongs to an experimental feature; see <see cref="AnalysisOperations.OptimizeImageAsync"/>.
/// </remarks>
public sealed class SlimResult
{
    /// <summary>Gets the image reference that was optimized.</summary>
    public required string OriginalImage { get; init; }

    /// <summary>Gets the tag Slim was asked to write the optimized image to.</summary>
    public required string OptimizedImage { get; init; }

    /// <summary>
    /// Gets a value indicating whether Slim reported success and the optimized image exists locally.
    /// </summary>
    public required bool Succeeded { get; init; }

    /// <summary>Gets the exit code of the Slim container.</summary>
    public long ExitCode { get; init; }

    /// <summary>Gets the size of the source image in bytes, when it could be read.</summary>
    public long? OriginalSizeBytes { get; init; }

    /// <summary>Gets the size of the optimized image in bytes, when it was produced.</summary>
    public long? OptimizedSizeBytes { get; init; }

    /// <summary>Gets Slim's combined console output, which is the record of what it did.</summary>
    public required string Output { get; init; }

    /// <summary>
    /// Gets the fraction of the original size the optimized image saved, or <see langword="null"/> when
    /// either size is unknown.
    /// </summary>
    public double? SizeReduction =>
        OriginalSizeBytes is > 0 && OptimizedSizeBytes is >= 0
            ? 1d - ((double)OptimizedSizeBytes.Value / OriginalSizeBytes.Value)
            : null;
}
