using System.Collections.Generic;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>The cluster picture.</summary>
public sealed class RedisClusterView
{
    /// <summary>Gets the cluster state word, normally <c>ok</c>.</summary>
    public string State { get; init; }

    /// <summary>Gets how many of the 16384 slots are assigned.</summary>
    public int SlotsAssigned { get; init; }

    /// <summary>Gets how many slots are healthy.</summary>
    public int SlotsOk { get; init; }

    /// <summary>Gets how many nodes the cluster knows about.</summary>
    public int KnownNodes { get; init; }

    /// <summary>Gets how many primaries the cluster has.</summary>
    public int Size { get; init; }

    /// <summary>Gets the nodes; never null.</summary>
    public IReadOnlyList<RedisClusterNodeView> Nodes { get; init; } = [];
}
