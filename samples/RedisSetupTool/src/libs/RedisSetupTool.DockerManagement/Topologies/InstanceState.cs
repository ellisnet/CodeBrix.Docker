namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>Where an instance is in its life.</summary>
public enum InstanceState
{
    /// <summary>Being built.</summary>
    Creating,

    /// <summary>Every node is running.</summary>
    Running,

    /// <summary>Some nodes are running and some are not.</summary>
    Partial,

    /// <summary>No node is running.</summary>
    Stopped,

    /// <summary>Creation failed.</summary>
    Failed,

    /// <summary>The state could not be read.</summary>
    Unknown,
}
