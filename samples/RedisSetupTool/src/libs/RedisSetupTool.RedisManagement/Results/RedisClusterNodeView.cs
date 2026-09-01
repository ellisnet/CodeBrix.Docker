namespace RedisSetupTool.RedisManagement.Results;

/// <summary>One node of a cluster.</summary>
public sealed class RedisClusterNodeView
{
    /// <summary>Gets the node's cluster id.</summary>
    public string NodeId { get; init; }

    /// <summary>Gets the address the node advertises.</summary>
    public string Endpoint { get; init; }

    /// <summary>Gets a value indicating whether the node owns slots.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Gets the id of the primary this node replicates, when it is a replica.</summary>
    public string PrimaryNodeId { get; init; }

    /// <summary>Gets the slot ranges the node owns.</summary>
    public string SlotRanges { get; init; }

    /// <summary>Gets a value indicating whether the link to the node is up.</summary>
    public bool IsConnected { get; init; }
}
