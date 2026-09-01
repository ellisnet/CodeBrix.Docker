namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>How a client connects to an instance.</summary>
public enum ConnectionShape
{
    /// <summary>One endpoint.</summary>
    Standalone,

    /// <summary>A writable primary and one or more read-only replicas.</summary>
    PrimaryReplica,

    /// <summary>Sentinels front a monitored primary.</summary>
    Sentinel,

    /// <summary>A sharded cluster reached through seed nodes.</summary>
    Cluster,

    /// <summary>Independent masters, connected to one at a time.</summary>
    IndependentQuorum,
}
