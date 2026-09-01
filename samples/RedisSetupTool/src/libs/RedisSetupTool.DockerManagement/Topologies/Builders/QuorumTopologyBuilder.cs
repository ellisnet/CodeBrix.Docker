using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// H1: five masters that know nothing about each other. There is deliberately no replication here -
/// independence is what makes the Redlock quorum mean anything.
/// </summary>
internal sealed class QuorumTopologyBuilder : ITopologyBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<TopologyId> Supported => [TopologyId.H1];

    /// <inheritdoc />
    public int StepCount(TopologyDescriptor descriptor) => descriptor.ContainerCount + 3;

    /// <inheritdoc />
    public async Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken)
    {
        await context.CreateNetworkAsync(cancellationToken).ConfigureAwait(false);

        var password = context.Password;
        var nodes = new List<TopologyNode>();
        var endpoints = new List<RedisEndpoint>();

        for (var index = 1; index <= context.Descriptor.ContainerCount; index++)
        {
            var hostPort = context.Ports.DataPorts[index - 1];
            var node = await context.StartNodeAsync(new NodePlan
            {
                RoleName = "master" + index.ToString(CultureInfo.InvariantCulture),
                RoleLabel = "master",
                Role = NodeRole.QuorumMaster,
                NodeIndex = index,
                ContainerPort = 6379,
                HostPort = hostPort,
                Command = NodeCommand(password),
            }, cancellationToken).ConfigureAwait(false);

            nodes.Add(node);
            endpoints.Add(new RedisEndpoint
            {
                Host = "127.0.0.1",
                Port = hostPort,
                Role = NodeRole.QuorumMaster,
                NodeIndex = index,
            });
        }

        await context.WaitAsync("all five masters to answer", TimeSpan.FromSeconds(45), async () =>
        {
            foreach (var node in nodes)
            {
                var ping = await context.ExecAsync(node.ContainerName,
                    context.RedisCli(6379, "ping"), cancellationToken).ConfigureAwait(false);
                if (!ping.Stdout.Contains("PONG", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var fsync = await context.ExecAsync(node.ContainerName,
                    context.RedisCli(6379, "CONFIG", "GET", "appendfsync"), cancellationToken)
                    .ConfigureAwait(false);
                if (!fsync.Stdout.Contains("always", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);

        var quorum = (context.Descriptor.ContainerCount / 2) + 1;

        return new TopologyBuildResult
        {
            Nodes = nodes,
            Connection = ConnectionInfoFactory.Build(ConnectionShape.IndependentQuorum, endpoints,
                password, notes:
                [
                    string.Format(CultureInfo.InvariantCulture, "Quorum is {0} of {1}.", quorum,
                        context.Descriptor.ContainerCount),
                    "AOF appendfsync always - a restarted node does not forget held locks.",
                    "These masters do not replicate to each other by design.",
                ]),
        };
    }

    private static IReadOnlyList<string> NodeCommand(string password)
    {
        var command = new List<string> { "--port", "6379" };

        if (!string.IsNullOrEmpty(password))
        {
            command.Add("--requirepass");
            command.Add(password);
            command.Add("--masterauth");
            command.Add(password);
        }

        command.Add("--dir");
        command.Add("/data");
        command.Add("--appendonly");
        command.Add("yes");
        command.Add("--appendfsync");
        command.Add("always");
        command.Add("--save");
        command.Add(string.Empty);
        return command;
    }
}
