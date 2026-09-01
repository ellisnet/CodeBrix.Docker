namespace CodeBrix.Docker;

/// <summary>
/// A container's memory usage split into the parts that matter operationally: application memory,
/// which drives OOM kills, and page cache, which the kernel reclaims for free.
/// </summary>
public sealed class MemoryBreakdownReport
{
    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the daemon returned live counters. This is
    /// <see langword="false"/> for a container that is not running, in which case the byte counts are
    /// zero or <see langword="null"/>.
    /// </summary>
    public bool HasLiveData { get; init; }

    /// <summary>
    /// Gets total memory charged to the container's cgroup, in bytes. This includes reclaimable page
    /// cache, so it routinely overstates what the application actually needs.
    /// </summary>
    public long UsageBytes { get; init; }

    /// <summary>
    /// Gets the container's configured hard memory limit in bytes, or <see langword="null"/> when no
    /// limit is set (in which case the container may use all host memory).
    /// </summary>
    public long? LimitBytes { get; init; }

    /// <summary>
    /// Gets anonymous (application) memory in bytes, from the cgroup <c>anon</c> counter. This is the
    /// part that cannot be reclaimed and therefore the part that triggers OOM kills.
    /// </summary>
    public long? AnonBytes { get; init; }

    /// <summary>
    /// Gets page-cache memory in bytes, from the cgroup <c>file</c> counter. The kernel reclaims this
    /// under pressure rather than killing the container.
    /// </summary>
    public long? FileBytes { get; init; }

    /// <summary>Gets kernel memory in bytes, from the cgroup <c>kernel</c> counter, when reported.</summary>
    public long? KernelBytes { get; init; }

    /// <summary>
    /// Gets <see cref="UsageBytes"/> as a percentage of <see cref="LimitBytes"/>, or
    /// <see langword="null"/> when no limit is set.
    /// </summary>
    public double? UsagePercent { get; init; }

    /// <summary>
    /// Gets <see cref="AnonBytes"/> as a percentage of <see cref="LimitBytes"/> — the number that
    /// actually predicts an OOM kill — or <see langword="null"/> when either input is missing.
    /// </summary>
    public double? EffectiveUsagePercent { get; init; }

    /// <summary>
    /// Gets a value indicating whether usage is dominated by reclaimable page cache (file memory more
    /// than twice anonymous memory and more than half of total usage), which makes the headline usage
    /// figure misleadingly high.
    /// </summary>
    public bool IsPageCacheDominated { get; init; }

    /// <summary>Gets a one-sentence, human-readable reading of the breakdown above.</summary>
    public string Interpretation { get; init; } = string.Empty;
}
