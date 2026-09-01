namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>What one node does inside its topology.</summary>
public enum NodeRole
{
    /// <summary>A writable primary.</summary>
    Primary,

    /// <summary>A replica of the primary.</summary>
    Replica,

    /// <summary>A sentinel.</summary>
    Sentinel,

    /// <summary>A cluster node holding slots.</summary>
    ClusterPrimary,

    /// <summary>A cluster node replicating a slot owner.</summary>
    ClusterReplica,

    /// <summary>One of the independent masters in a lock quorum.</summary>
    QuorumMaster,
}
