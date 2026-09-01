namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The resource limits to apply to a running container. Every value is optional.</summary>
public sealed class ResourceLimitUpdate
{
    /// <summary>Gets or sets the CPU quota expressed in cores.</summary>
    public double? Cpus { get; set; }

    /// <summary>Gets or sets the CPU set to pin the container to.</summary>
    public string CpusetCpus { get; set; }

    /// <summary>Gets or sets the relative CPU share weight.</summary>
    public long? CpuShares { get; set; }

    /// <summary>Gets or sets the memory limit, in bytes.</summary>
    public long? MemoryBytes { get; set; }

    /// <summary>Gets or sets the soft memory reservation, in bytes.</summary>
    public long? MemoryReservationBytes { get; set; }

    /// <summary>Gets or sets the memory-plus-swap limit, in bytes.</summary>
    public long? MemorySwapBytes { get; set; }

    /// <summary>Gets or sets the process-count limit.</summary>
    public long? PidsLimit { get; set; }
}
