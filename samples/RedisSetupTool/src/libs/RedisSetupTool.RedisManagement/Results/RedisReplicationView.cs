using System.Collections.Generic;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>The replication picture from one node's point of view.</summary>
public sealed class RedisReplicationView
{
    /// <summary>Gets the primary's address.</summary>
    public string MasterEndpoint { get; init; }

    /// <summary>Gets this node's role.</summary>
    public string Role { get; init; }

    /// <summary>Gets how many replicas are attached.</summary>
    public int ConnectedReplicas { get; init; }

    /// <summary>Gets the replicas; never null.</summary>
    public IReadOnlyList<RedisReplicaView> Replicas { get; init; } = [];

    /// <summary>Gets the link state, when this node is a replica.</summary>
    public string MasterLinkStatus { get; init; }

    /// <summary>Gets the failover state.</summary>
    public string FailoverState { get; init; }
}
