using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// B1: a primary and one replica. The replica follows the primary at the network gateway address and
/// announces itself the same way, so every address in the replication view is host-reachable too.
/// </summary>
internal sealed class ReplicaTopologyBuilder : ITopologyBuilder
{
    /// <inheritdoc />
    public IReadOnlyList<TopologyId> Supported => [TopologyId.B1];

    /// <inheritdoc />
    public int StepCount(TopologyDescriptor descriptor) => 6;

    /// <inheritdoc />
    public async Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken)
    {
        var gateway = await context.CreateNetworkAsync(cancellationToken).ConfigureAwait(false);
        var password = context.Password;
        var primaryPort = context.Ports.DataPorts[0];
        var replicaPort = context.Ports.DataPorts[1];

        var primary = await context.StartNodeAsync(new NodePlan
        {
            RoleName = "primary",
            RoleLabel = "primary",
            Role = NodeRole.Primary,
            NodeIndex = 1,
            ContainerPort = 6379,
            HostPort = primaryPort,
            Command = NodeCommand(password, gateway, primaryPort, replicaOfPort: null),
        }, cancellationToken).ConfigureAwait(false);

        await context.WaitForPongAsync(primary, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        var replica = await context.StartNodeAsync(new NodePlan
        {
            RoleName = "replica1",
            RoleLabel = "replica",
            Role = NodeRole.Replica,
            NodeIndex = 2,
            ContainerPort = 6379,
            HostPort = replicaPort,
            Command = NodeCommand(password, gateway, replicaPort, primaryPort),
        }, cancellationToken).ConfigureAwait(false);

        await context.WaitForPongAsync(replica, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        await context.WaitAsync("replication to be established", TimeSpan.FromSeconds(30), async () =>
        {
            var onPrimary = await context.ExecAsync(primary.ContainerName,
                context.RedisCli(6379, "INFO", "replication"), cancellationToken).ConfigureAwait(false);
            var onReplica = await context.ExecAsync(replica.ContainerName,
                context.RedisCli(6379, "INFO", "replication"), cancellationToken).ConfigureAwait(false);

            return onPrimary.Stdout.Contains("connected_slaves:1", StringComparison.Ordinal)
                && onReplica.Stdout.Contains("master_link_status:up", StringComparison.Ordinal);
        }, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RedisEndpoint> endpoints =
        [
            new RedisEndpoint
            {
                Host = "127.0.0.1", Port = primaryPort, Role = NodeRole.Primary, NodeIndex = 1,
            },
            new RedisEndpoint
            {
                Host = "127.0.0.1", Port = replicaPort, Role = NodeRole.Replica, NodeIndex = 2,
            },
        ];

        return new TopologyBuildResult
        {
            Nodes = [primary, replica],
            Connection = ConnectionInfoFactory.Build(ConnectionShape.PrimaryReplica, endpoints,
                password, notes:
                [
                    "Writes go to the primary; the replica answers reads.",
                    "The replica follows the primary at " + gateway + ":" + primaryPort + ".",
                ]),
        };
    }

    private static IReadOnlyList<string> NodeCommand(string password, string gateway, int announcePort,
        int? replicaOfPort)
    {
        var command = new List<string> { "--port", "6379" };

        if (!string.IsNullOrEmpty(password))
        {
            command.Add("--requirepass");
            command.Add(password);
            command.Add("--masterauth");
            command.Add(password);
        }

        command.Add("--replica-announce-ip");
        command.Add(gateway);
        command.Add("--replica-announce-port");
        command.Add(announcePort.ToString(CultureInfo.InvariantCulture));

        if (replicaOfPort.HasValue)
        {
            command.Add("--replicaof");
            command.Add(gateway);
            command.Add(replicaOfPort.Value.ToString(CultureInfo.InvariantCulture));
        }

        return command;
    }
}
