namespace RedisSetupTool.DockerManagement.Models;

/// <summary>CPU, memory, OOM and health for one container, in one call.</summary>
public sealed class DiagnosticsReport
{
    /// <summary>Gets the container id.</summary>
    public string ContainerId { get; init; }

    /// <summary>Gets the container name.</summary>
    public string ContainerName { get; init; }

    /// <summary>Gets the state word.</summary>
    public string Status { get; init; }

    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets a one-line summary of the four sub-reports.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the CPU throttling report.</summary>
    public CpuThrottlingInfo Cpu { get; init; }

    /// <summary>Gets the memory breakdown.</summary>
    public MemoryBreakdownInfo Memory { get; init; }

    /// <summary>Gets the out-of-memory report.</summary>
    public OomInfo Oom { get; init; }

    /// <summary>Gets the health report.</summary>
    public HealthInfo Health { get; init; }
}
