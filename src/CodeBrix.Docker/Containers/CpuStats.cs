using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A CPU sample for a container.
/// </summary>
/// <remarks>
/// The daemon returns an empty <c>cpu_stats</c> object for containers that are not running, so every
/// member here can be <see langword="null"/>.
/// </remarks>
public sealed class CpuStats
{
    /// <summary>Gets the container's cumulative CPU usage.</summary>
    [JsonPropertyName("cpu_usage")]
    public CpuUsage CpuUsage { get; init; }

    /// <summary>Gets the host's cumulative CPU time, the denominator of the CPU-percentage formula.</summary>
    [JsonPropertyName("system_cpu_usage")]
    public long? SystemCpuUsage { get; init; }

    /// <summary>Gets the number of CPUs visible to the container.</summary>
    [JsonPropertyName("online_cpus")]
    public int? OnlineCpus { get; init; }

    /// <summary>Gets the CFS throttling counters.</summary>
    [JsonPropertyName("throttling_data")]
    public ThrottlingData ThrottlingData { get; init; }
}
