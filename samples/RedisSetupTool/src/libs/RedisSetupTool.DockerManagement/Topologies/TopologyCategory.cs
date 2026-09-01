namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>How the catalog groups topologies in the picker.</summary>
public enum TopologyCategory
{
    /// <summary>One node.</summary>
    SingleNode,

    /// <summary>Asynchronous replication.</summary>
    Replication,

    /// <summary>Automatic failover.</summary>
    HighAvailability,

    /// <summary>Sharded cluster.</summary>
    Cluster,

    /// <summary>A different version or fork.</summary>
    VersionMatrix,

    /// <summary>A feature showcase.</summary>
    Features,

    /// <summary>An operational showcase.</summary>
    Operational,

    /// <summary>Distributed locking.</summary>
    Locking,
}
