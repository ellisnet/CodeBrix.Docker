using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>What a builder produced.</summary>
internal sealed class TopologyBuildResult
{
    /// <summary>Gets or sets the nodes, in index order.</summary>
    internal IReadOnlyList<TopologyNode> Nodes { get; set; } = [];

    /// <summary>Gets or sets everything a client needs to connect.</summary>
    internal ConnectionInfo Connection { get; set; }
}
