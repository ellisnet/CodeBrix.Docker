namespace RedisSetupTool.RedisManagement;

/// <summary>How a client reaches a Redis deployment.</summary>
public enum RedisConnectionShape
{
    /// <summary>One node.</summary>
    Standalone,

    /// <summary>A writable primary and one or more replicas.</summary>
    PrimaryReplica,

    /// <summary>Sentinels front a monitored primary.</summary>
    Sentinel,

    /// <summary>A sharded cluster reached through seed nodes.</summary>
    Cluster,

    /// <summary>Independent masters, connected to one at a time.</summary>
    IndependentQuorum,
}
