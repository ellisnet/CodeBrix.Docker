namespace CodeBrix.Docker;

/// <summary>
/// The outcome of <see cref="ImageOperations.PruneBuildCacheAsync"/>.
/// </summary>
public sealed class BuildCachePruneResult
{
    /// <summary>
    /// Gets the reclaimed space exactly as the CLI's final <c>Total:</c> line reports it, for example
    /// <c>"0B"</c> or <c>"14.56GB"</c>, or <see langword="null"/> when the output has no such line.
    /// </summary>
    public string ReclaimedSpaceText { get; init; }

    /// <summary>
    /// Gets <see cref="ReclaimedSpaceText"/> converted to bytes, or <see langword="null"/> when it is
    /// missing or cannot be parsed. Decimal units (<c>kB</c>, <c>MB</c>, <c>GB</c>, <c>TB</c>) are
    /// 1000-based and binary units (<c>KiB</c>, <c>MiB</c>, <c>GiB</c>, <c>TiB</c>) are 1024-based, so the
    /// value is approximate: the CLI rounds what it prints.
    /// </summary>
    public long? ReclaimedBytes { get; init; }

    /// <summary>
    /// Gets everything the CLI wrote to standard output and standard error, in arrival order: the list of
    /// removed cache entries, any warnings, and the final <c>Total:</c> line.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// The storage limit flag the prune passed for <see cref="BuildCachePruneOptions.KeepStorageBytes"/>:
    /// <c>--reserved-space</c> with buildx 0.19 or later, <c>--keep-storage</c> with an older buildx or the
    /// legacy builder; <see langword="null"/> when no limit was requested.
    /// </summary>
    public string StorageFlag { get; init; }
}
