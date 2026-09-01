using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A memory sample for a container.
/// </summary>
/// <remarks>
/// The daemon returns an empty <c>memory_stats</c> object for containers that are not running, so
/// every member here can be <see langword="null"/>.
/// </remarks>
public sealed class MemoryStats
{
    /// <summary>
    /// Gets total memory charged to the container's cgroup, in bytes. This includes reclaimable page
    /// cache, so it routinely overstates what the application actually needs — compare
    /// <see cref="AnonBytes"/> against <see cref="Limit"/> for the number that predicts OOM kills.
    /// </summary>
    [JsonPropertyName("usage")]
    public long? Usage { get; init; }

    /// <summary>Gets the peak usage, reported only under cgroup v1.</summary>
    [JsonPropertyName("max_usage")]
    public long? MaxUsage { get; init; }

    /// <summary>Gets the memory limit in bytes; the host's total memory when no limit is set.</summary>
    [JsonPropertyName("limit")]
    public long? Limit { get; init; }

    /// <summary>Gets the number of times the cgroup hit its limit, reported only under cgroup v1.</summary>
    [JsonPropertyName("failcnt")]
    public long? Failcnt { get; init; }

    /// <summary>
    /// Gets the raw <c>memory.stat</c> breakdown. Under cgroup v2 the useful keys are <c>anon</c>
    /// (application memory), <c>file</c> (page cache, reclaimable), <c>kernel</c>, <c>slab</c>,
    /// <c>shmem</c> and <c>pgfault</c>.
    /// </summary>
    [JsonPropertyName("stats")]
    [JsonConverter(typeof(TolerantLongDictionaryConverter))]
    public IReadOnlyDictionary<string, long> Stats { get; init; }

    /// <summary>
    /// Gets anonymous (application) memory in bytes — the part of usage that cannot be reclaimed and
    /// therefore drives OOM kills.
    /// </summary>
    [JsonIgnore]
    public long? AnonBytes => Lookup("anon") ?? Lookup("rss");

    /// <summary>Gets page-cache memory in bytes, which the kernel can reclaim under pressure.</summary>
    [JsonIgnore]
    public long? FileBytes => Lookup("file") ?? Lookup("cache");

    /// <summary>Gets kernel memory in bytes, when the daemon reports it.</summary>
    [JsonIgnore]
    public long? KernelBytes => Lookup("kernel") ?? Lookup("kernel_stack");

    /// <summary>Gets slab-allocator memory in bytes, when the daemon reports it.</summary>
    [JsonIgnore]
    public long? SlabBytes => Lookup("slab");

    /// <summary>Gets shared-memory bytes, when the daemon reports it.</summary>
    [JsonIgnore]
    public long? ShmemBytes => Lookup("shmem");

    /// <summary>
    /// Looks up one <c>memory.stat</c> key.
    /// </summary>
    /// <param name="key">The cgroup key, for example <c>anon</c>.</param>
    /// <returns>The value, or <see langword="null"/> when the key is absent.</returns>
    public long? Lookup(string key) =>
        Stats is not null && Stats.TryGetValue(key, out var value) ? value : null;
}
