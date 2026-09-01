namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The resource limits a container was created with.</summary>
public sealed class ResourceLimitInfo
{
    /// <summary>Gets the CPU quota expressed in cores.</summary>
    public double? Cpus { get; init; }

    /// <summary>Gets the CPU set the container is pinned to.</summary>
    public string CpusetCpus { get; init; }

    /// <summary>Gets the relative CPU share weight.</summary>
    public long CpuShares { get; init; }

    /// <summary>Gets the memory limit, in bytes.</summary>
    public long MemoryBytes { get; init; }

    /// <summary>Gets the soft memory reservation, in bytes.</summary>
    public long MemoryReservationBytes { get; init; }

    /// <summary>Gets the memory-plus-swap limit, in bytes.</summary>
    public long MemorySwapBytes { get; init; }

    /// <summary>Gets the process-count limit.</summary>
    public long? PidsLimit { get; init; }

    /// <summary>Gets a value indicating whether the container is privileged.</summary>
    public bool Privileged { get; init; }

    /// <summary>Gets the restart policy name.</summary>
    public string RestartPolicy { get; init; }

    /// <summary>Gets the logging driver name.</summary>
    public string LogDriver { get; init; }

    /// <summary>Gets a value indicating whether a CPU limit is set.</summary>
    public bool HasCpuLimit { get; init; }

    /// <summary>Gets a value indicating whether a memory limit is set.</summary>
    public bool HasMemoryLimit { get; init; }

    /// <summary>Gets a value indicating whether swap is disabled by an equal swap limit.</summary>
    public bool IsSwapDisabled { get; init; }
}
