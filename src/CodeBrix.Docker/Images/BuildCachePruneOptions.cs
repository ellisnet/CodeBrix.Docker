using System;

namespace CodeBrix.Docker;

/// <summary>
/// Options for <see cref="ImageOperations.PruneBuildCacheAsync"/>, which runs
/// <c>docker builder prune --force</c>.
/// </summary>
/// <remarks>
/// WARNING: the prune is ENGINE-WIDE. It removes unused BuildKit cache belonging to EVERY project that
/// builds on the same engine, not only the cache of images this library built. Build cache carries no
/// labels, so the prune cannot be narrowed to one tool's or one test suite's builds;
/// <see cref="OlderThan"/> and <see cref="KeepStorageBytes"/> are the only limits available.
/// </remarks>
public sealed class BuildCachePruneOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to pass <c>--all</c>. When <see langword="true"/>, the prune
    /// also removes cache that is still referenced by existing images and the internal/frontend images
    /// BuildKit keeps, not only dangling cache. Every later build on the engine then starts cold. The
    /// default is <see langword="false"/>.
    /// </summary>
    public bool All { get; set; }

    /// <summary>
    /// Gets or sets the amount of build cache, in bytes, to keep. Zero or less (the default) passes
    /// nothing, so no amount is kept back. The flag follows the installed buildx: <c>--reserved-space
    /// &lt;bytes&gt;</c> with buildx 0.19 or later (which deprecates the old name), <c>--keep-storage
    /// &lt;bytes&gt;</c> with an older buildx or with no buildx plugin at all. The prune reads
    /// <c>docker buildx version</c> once per <see cref="ImageOperations"/> instance to decide, and
    /// <see cref="BuildCachePruneResult.StorageFlag"/> tells which flag was used.
    /// </summary>
    public long KeepStorageBytes { get; set; }

    /// <summary>
    /// Gets or sets a minimum age for the cache entries to remove (<c>--filter until=&lt;N&gt;h</c>).
    /// The value is sent as whole hours, rounded up, with a minimum of one hour. <see langword="null"/>
    /// (the default) applies no age filter.
    /// </summary>
    public TimeSpan? OlderThan { get; set; }
}
