namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>The host port ranges the allocator draws from.</summary>
public sealed class PortAllocationOptions
{
    /// <summary>Gets or sets the first data port.</summary>
    public int DataPortRangeStart { get; set; } = 6400;

    /// <summary>Gets or sets the last data port.</summary>
    public int DataPortRangeEnd { get; set; } = 6999;

    /// <summary>Gets or sets the first sentinel port.</summary>
    public int SentinelPortRangeStart { get; set; } = 26400;

    /// <summary>Gets or sets the last sentinel port.</summary>
    public int SentinelPortRangeEnd { get; set; } = 26999;

    /// <summary>Gets or sets the first cluster data port; bus ports live at the offset above.</summary>
    public int ClusterPortRangeStart { get; set; } = 7400;

    /// <summary>Gets or sets the last cluster data port.</summary>
    public int ClusterPortRangeEnd { get; set; } = 7999;

    /// <summary>Gets or sets how far above a cluster data port its bus port sits.</summary>
    public int BusPortOffset { get; set; } = 10000;
}
