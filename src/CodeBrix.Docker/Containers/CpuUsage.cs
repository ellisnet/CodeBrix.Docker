using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Cumulative CPU time consumed by a container, in nanoseconds.
/// </summary>
public sealed class CpuUsage
{
    /// <summary>Gets the total CPU time consumed.</summary>
    [JsonPropertyName("total_usage")]
    public long? TotalUsage { get; init; }

    /// <summary>Gets the CPU time spent in kernel mode.</summary>
    [JsonPropertyName("usage_in_kernelmode")]
    public long? UsageInKernelMode { get; init; }

    /// <summary>Gets the CPU time spent in user mode.</summary>
    [JsonPropertyName("usage_in_usermode")]
    public long? UsageInUserMode { get; init; }

    /// <summary>Gets the per-CPU breakdown, reported only under cgroup v1.</summary>
    [JsonPropertyName("percpu_usage")]
    public IReadOnlyList<long> PerCpuUsage { get; init; }
}
