namespace RedisSetupTool.DockerManagement.Models;

/// <summary>Where a container's memory went.</summary>
public sealed class MemoryBreakdownInfo
{
    /// <summary>Gets a value indicating whether the daemon supplied usable figures.</summary>
    public bool HasLiveData { get; init; }

    /// <summary>Gets memory in use, in bytes.</summary>
    public long UsageBytes { get; init; }

    /// <summary>Gets the memory limit, in bytes.</summary>
    public long? LimitBytes { get; init; }

    /// <summary>Gets anonymous memory, in bytes.</summary>
    public long? AnonBytes { get; init; }

    /// <summary>Gets page-cache memory, in bytes.</summary>
    public long? FileBytes { get; init; }

    /// <summary>Gets kernel memory, in bytes.</summary>
    public long? KernelBytes { get; init; }

    /// <summary>Gets memory use as a percentage of the limit.</summary>
    public double? UsagePercent { get; init; }

    /// <summary>Gets memory use excluding reclaimable page cache, as a percentage of the limit.</summary>
    public double? EffectiveUsagePercent { get; init; }

    /// <summary>Gets a value indicating whether page cache accounts for most of the usage.</summary>
    public bool IsPageCacheDominated { get; init; }

    /// <summary>Gets a sentence explaining the numbers.</summary>
    public string Interpretation { get; init; } = string.Empty;
}
