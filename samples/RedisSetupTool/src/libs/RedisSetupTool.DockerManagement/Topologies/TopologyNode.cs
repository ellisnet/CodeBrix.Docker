namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One container in an instance.</summary>
public sealed class TopologyNode
{
    /// <summary>Gets the container id.</summary>
    public string ContainerId { get; init; }

    /// <summary>Gets the container name.</summary>
    public string ContainerName { get; init; }

    /// <summary>Gets what the node does.</summary>
    public NodeRole Role { get; init; }

    /// <summary>Gets the one-based node index.</summary>
    public int NodeIndex { get; init; }

    /// <summary>Gets the port inside the container.</summary>
    public int ContainerPort { get; init; }

    /// <summary>Gets the published host port.</summary>
    public int HostPort { get; init; }

    /// <summary>Gets the published cluster-bus port; D2 only.</summary>
    public int? BusHostPort { get; init; }

    /// <summary>Gets the volume mounted at <c>/data</c>.</summary>
    public string VolumeName { get; init; }

    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the container state word.</summary>
    public string State { get; init; }
}
