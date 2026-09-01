using System;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One sample of a container's live resource use.</summary>
public sealed class ContainerStatsSample
{
    /// <summary>Gets the container id.</summary>
    public string ContainerId { get; init; }

    /// <summary>Gets the container name.</summary>
    public string Name { get; init; }

    /// <summary>Gets when the sample was read.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Gets a value indicating whether the daemon supplied usable figures.</summary>
    public bool HasLiveData { get; init; }

    /// <summary>Gets CPU use as a percentage of one host CPU-second per second.</summary>
    public double? CpuPercent { get; init; }

    /// <summary>Gets memory in use, in bytes.</summary>
    public long? MemoryUsageBytes { get; init; }

    /// <summary>Gets the memory limit, in bytes.</summary>
    public long? MemoryLimitBytes { get; init; }

    /// <summary>Gets memory use as a percentage of the limit.</summary>
    public double? MemoryPercent { get; init; }

    /// <summary>Gets memory use excluding reclaimable page cache, as a percentage of the limit.</summary>
    public double? EffectiveMemoryPercent { get; init; }

    /// <summary>Gets anonymous memory, in bytes.</summary>
    public long? AnonBytes { get; init; }

    /// <summary>Gets page-cache memory, in bytes.</summary>
    public long? FileBytes { get; init; }

    /// <summary>Gets the current process count.</summary>
    public long? PidsCurrent { get; init; }

    /// <summary>Gets the process-count limit.</summary>
    public long? PidsLimit { get; init; }

    /// <summary>Gets bytes received on all networks.</summary>
    public long NetworkRxBytes { get; init; }

    /// <summary>Gets bytes sent on all networks.</summary>
    public long NetworkTxBytes { get; init; }

    /// <summary>Gets bytes read from block devices.</summary>
    public long BlockReadBytes { get; init; }

    /// <summary>Gets bytes written to block devices.</summary>
    public long BlockWriteBytes { get; init; }

    /// <summary>Gets the share of CPU periods that were throttled.</summary>
    public double? ThrottleRatio { get; init; }
}
