using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One created Redis instance, rebuilt from labels alone.</summary>
public sealed class TopologyInstance
{
    /// <summary>Gets the instance id, for example <c>d2-1a2b3c4d</c>.</summary>
    public string InstanceId { get; init; }

    /// <summary>Gets the friendly name.</summary>
    public string InstanceName { get; init; }

    /// <summary>Gets the topology.</summary>
    public TopologyId TopologyId { get; init; }

    /// <summary>Gets the topology code.</summary>
    public string TopologyCode { get; init; }

    /// <summary>Gets the image the nodes run.</summary>
    public string Image { get; init; }

    /// <summary>Gets when the instance was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets where the instance is in its life.</summary>
    public InstanceState State { get; init; }

    /// <summary>Gets a one-line status, for example <c>6 of 6 running</c>.</summary>
    public string StatusText { get; init; }

    /// <summary>Gets the instance's own bridge network.</summary>
    public string NetworkName { get; init; }

    /// <summary>Gets the gateway address the nodes announce, when the topology announces one.</summary>
    public string AnnounceIp { get; init; }

    /// <summary>Gets the volumes; never null.</summary>
    public IReadOnlyList<string> VolumeNames { get; init; } = [];

    /// <summary>Gets the nodes, in index order; never null.</summary>
    public IReadOnlyList<TopologyNode> Nodes { get; init; } = [];

    /// <summary>Gets everything needed to connect.</summary>
    public ConnectionInfo Connection { get; init; }

    /// <summary>Gets how many nodes are running.</summary>
    public int RunningNodeCount
    {
        get
        {
            var count = 0;
            foreach (var node in Nodes)
            {
                if (node.IsRunning)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets how many nodes the instance has.</summary>
    public int NodeCount => Nodes.Count;
}
