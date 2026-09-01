using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// D2: three primaries and three replicas. Container port equals host port here and nowhere else,
/// which keeps the bus-port arithmetic trivial and matches the shape that was proven end to end.
/// Every node announces the network gateway, so a client on the host follows MOVED correctly.
/// </summary>
internal sealed class ClusterTopologyBuilder : ITopologyBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<TopologyId> Supported => [TopologyId.D2];

    /// <inheritdoc />
    public int StepCount(TopologyDescriptor descriptor) => descriptor.ContainerCount + 5;

    /// <inheritdoc />
    public async Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken)
    {
        var gateway = await context.CreateNetworkAsync(cancellationToken).ConfigureAwait(false);
        var password = context.Password;
        var dataPorts = context.Ports.DataPorts;
        var busPorts = context.Ports.BusPorts;
        var nodeTimeout = context.ParameterInt("nodeTimeoutMs", 5000);

        var nodes = new List<TopologyNode>();
        for (var index = 1; index <= context.Descriptor.ContainerCount; index++)
        {
            var port = dataPorts[index - 1];
            nodes.Add(await context.StartNodeAsync(new NodePlan
            {
                RoleName = "node" + index.ToString(CultureInfo.InvariantCulture),
                RoleLabel = "cluster-primary",
                Role = NodeRole.ClusterPrimary,
                NodeIndex = index,
                ContainerPort = port,
                HostPort = port,
                BusHostPort = busPorts[index - 1],
                Command = NodeCommand(password, gateway, port, busPorts[index - 1], nodeTimeout),
            }, cancellationToken).ConfigureAwait(false));
        }

        await context.WaitAsync("every node to answer", TimeSpan.FromSeconds(60), async () =>
        {
            foreach (var node in nodes)
            {
                var ping = await context.ExecAsync(node.ContainerName,
                    context.RedisCli(node.ContainerPort, "ping"), cancellationToken)
                    .ConfigureAwait(false);
                if (!ping.Stdout.Contains("PONG", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);

        context.Report("Creating the cluster");
        var create = await context.ExecAsync(nodes[0].ContainerName,
            CreateCommand(password, gateway, dataPorts), cancellationToken).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            throw new DockerManagementException(
                "redis-cli --cluster create failed: " + Trim(create.Stdout + create.Stderr));
        }

        //The cluster reports fail for several seconds after creation and then ok, so this polls.
        await context.WaitAsync("cluster_state:ok", TimeSpan.FromSeconds(90), async () =>
        {
            var info = await context.ExecAsync(nodes[0].ContainerName,
                context.RedisCli(nodes[0].ContainerPort, "cluster", "info"), cancellationToken)
                .ConfigureAwait(false);
            return info.Stdout.Contains("cluster_state:ok", StringComparison.Ordinal);
        }, cancellationToken).ConfigureAwait(false);

        var topology = await context.ExecAsync(nodes[0].ContainerName,
            context.RedisCli(nodes[0].ContainerPort, "cluster", "nodes"), cancellationToken)
            .ConfigureAwait(false);

        var notes = new List<string>
        {
            "Nodes advertise " + gateway + ", which is reachable from the host and from other containers.",
        };

        var resolved = new List<TopologyNode>(nodes.Count);
        var endpoints = new List<RedisEndpoint>(nodes.Count);
        foreach (var node in nodes)
        {
            var line = FindLine(topology.Stdout, gateway, node.HostPort);
            var isPrimary = line is null || line.Contains("master", StringComparison.Ordinal);
            var slots = isPrimary ? ReadSlots(line) : null;

            resolved.Add(new TopologyNode
            {
                ContainerId = node.ContainerId,
                ContainerName = node.ContainerName,
                Role = isPrimary ? NodeRole.ClusterPrimary : NodeRole.ClusterReplica,
                NodeIndex = node.NodeIndex,
                ContainerPort = node.ContainerPort,
                HostPort = node.HostPort,
                BusHostPort = node.BusHostPort,
                VolumeName = node.VolumeName,
                IsRunning = true,
                State = "running",
            });

            endpoints.Add(new RedisEndpoint
            {
                Host = gateway,
                Port = node.HostPort,
                Role = isPrimary ? NodeRole.ClusterPrimary : NodeRole.ClusterReplica,
                NodeIndex = node.NodeIndex,
            });

            if (!string.IsNullOrEmpty(slots))
            {
                notes.Add("node" + node.NodeIndex.ToString(CultureInfo.InvariantCulture)
                    + " slots " + slots);
            }
        }

        return new TopologyBuildResult
        {
            Nodes = resolved,
            Connection = ConnectionInfoFactory.Build(ConnectionShape.Cluster, endpoints, password,
                notes: notes),
        };
    }

    /// <summary>Builds the cluster-create command run inside the first node.</summary>
    /// <param name="password">The shared password, when there is one.</param>
    /// <param name="gateway">The announce address.</param>
    /// <param name="ports">The published data ports.</param>
    /// <returns>The command.</returns>
    internal static string[] CreateCommand(string password, string gateway, IReadOnlyList<int> ports)
    {
        var command = new List<string> { "redis-cli" };
        if (!string.IsNullOrEmpty(password))
        {
            command.Add("-a");
            command.Add(password);
            command.Add("--no-auth-warning");
        }

        command.Add("--cluster");
        command.Add("create");
        foreach (var port in ports)
        {
            command.Add(gateway + ":" + port.ToString(CultureInfo.InvariantCulture));
        }

        command.Add("--cluster-replicas");
        command.Add("1");
        command.Add("--cluster-yes");
        return [.. command];
    }

    /// <summary>Finds the <c>CLUSTER NODES</c> line describing one announced address.</summary>
    /// <param name="text">The command output.</param>
    /// <param name="gateway">The announce address.</param>
    /// <param name="port">The announced port.</param>
    /// <returns>The line, or null when the node is not listed.</returns>
    internal static string FindLine(string text, string gateway, int port)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var needle = gateway + ":" + port.ToString(CultureInfo.InvariantCulture) + "@";
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains(needle, StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>Reads the slot ranges off a <c>CLUSTER NODES</c> line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The ranges, space separated, or an empty string.</returns>
    internal static string ReadSlots(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var slots = new List<string>();
        for (var index = 8; index < fields.Length; index++)
        {
            if (fields[index].Length > 0 && char.IsAsciiDigit(fields[index][0]))
            {
                slots.Add(fields[index]);
            }
        }

        return string.Join(' ', slots);
    }

    private static IReadOnlyList<string> NodeCommand(string password, string gateway, int port,
        int busPort, int nodeTimeoutMs)
    {
        var command = new List<string>
        {
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "--cluster-enabled", "yes",
            "--cluster-config-file", "nodes.conf",
            "--cluster-node-timeout", nodeTimeoutMs.ToString(CultureInfo.InvariantCulture),
            "--appendonly", "yes",
            "--dir", "/data",
            "--cluster-announce-ip", gateway,
            "--cluster-announce-port", port.ToString(CultureInfo.InvariantCulture),
            "--cluster-announce-bus-port", busPort.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(password))
        {
            command.Add("--requirepass");
            command.Add(password);
            command.Add("--masterauth");
            command.Add(password);
        }

        return command;
    }

    private static string Trim(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty
            : (text.Length > 400 ? text[..400] : text).Trim();
}
