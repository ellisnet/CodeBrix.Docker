namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>The thirteen approved Redis topologies.</summary>
public enum TopologyId
{
    /// <summary>Plain standalone.</summary>
    A1,

    /// <summary>Standalone with a password.</summary>
    A2,

    /// <summary>Standalone with ACL users.</summary>
    A3,

    /// <summary>Standalone with RDB and AOF persistence.</summary>
    A5,

    /// <summary>Standalone with a memory cap and an eviction policy.</summary>
    A6,

    /// <summary>Primary and one replica.</summary>
    B1,

    /// <summary>Sentinel: one primary, two replicas, three sentinels.</summary>
    C1,

    /// <summary>Cluster: three primaries, three replicas.</summary>
    D2,

    /// <summary>Redis 6.2, the compatibility floor.</summary>
    E3,

    /// <summary>Valkey 8.1.</summary>
    E4,

    /// <summary>Redis 8 with its bundled modules.</summary>
    F3,

    /// <summary>Memory-capped container, for the diagnostics tier.</summary>
    G1,

    /// <summary>Five independent masters, for Redlock.</summary>
    H1,
}
