using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// C1: one primary, two replicas and three sentinels. Everything a sentinel hands a client is a
/// gateway address and a published host port, so a sentinel-aware client running on the host can
/// actually follow it - monitoring by container alias would hand out <c>primary:6379</c>, which no
/// process outside the network can resolve.
/// </summary>
internal sealed class SentinelTopologyBuilder : ITopologyBuilder
{
    /// <summary>The port every sentinel listens on inside its container.</summary>
    internal const int SentinelContainerPort = 26379;

    /// <inheritdoc />
    public IReadOnlyList<TopologyId> Supported => [TopologyId.C1];

    /// <inheritdoc />
    public int StepCount(TopologyDescriptor descriptor) => 12;

    /// <inheritdoc />
    public async Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken)
    {
        var gateway = await context.CreateNetworkAsync(cancellationToken).ConfigureAwait(false);
        var password = context.Password;
        var service = context.Parameter("serviceName");
        if (string.IsNullOrEmpty(service))
        {
            service = "mymaster";
        }

        var dataPorts = context.Ports.DataPorts;
        var sentinelPorts = context.Ports.SentinelPorts;

        var primary = await context.StartNodeAsync(new NodePlan
        {
            RoleName = "primary",
            RoleLabel = "primary",
            Role = NodeRole.Primary,
            NodeIndex = 1,
            ContainerPort = 6379,
            HostPort = dataPorts[0],
            Command = DataCommand(password, gateway, dataPorts[0], replicaOfPort: null),
        }, cancellationToken).ConfigureAwait(false);

        await context.WaitForPongAsync(primary, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        var nodes = new List<TopologyNode> { primary };

        for (var replicaIndex = 1; replicaIndex <= 2; replicaIndex++)
        {
            var hostPort = dataPorts[replicaIndex];
            nodes.Add(await context.StartNodeAsync(new NodePlan
            {
                RoleName = "replica" + replicaIndex.ToString(CultureInfo.InvariantCulture),
                RoleLabel = "replica",
                Role = NodeRole.Replica,
                NodeIndex = replicaIndex + 1,
                ContainerPort = 6379,
                HostPort = hostPort,
                Command = DataCommand(password, gateway, hostPort, dataPorts[0]),
            }, cancellationToken).ConfigureAwait(false));
        }

        await context.WaitAsync("both replicas to attach", TimeSpan.FromSeconds(45), async () =>
        {
            var info = await context.ExecAsync(primary.ContainerName,
                context.RedisCli(6379, "INFO", "replication"), cancellationToken).ConfigureAwait(false);
            return info.Stdout.Contains("connected_slaves:2", StringComparison.Ordinal);
        }, cancellationToken).ConfigureAwait(false);

        for (var sentinelIndex = 1; sentinelIndex <= 3; sentinelIndex++)
        {
            var hostPort = sentinelPorts[sentinelIndex - 1];
            nodes.Add(await context.StartNodeAsync(new NodePlan
            {
                RoleName = "sentinel" + sentinelIndex.ToString(CultureInfo.InvariantCulture),
                RoleLabel = "sentinel",
                Role = NodeRole.Sentinel,
                NodeIndex = 3 + sentinelIndex,
                ContainerPort = SentinelContainerPort,
                HostPort = hostPort,
                //The one place an entrypoint override is right: redis-sentinel needs a config file
                //  it can rewrite, and this design mounts no host path to put one at.
                Entrypoint = ["sh", "-c"],
                Command = [SentinelScript(gateway, hostPort, dataPorts[0], password, service)],
            }, cancellationToken).ConfigureAwait(false));
        }

        var firstSentinel = nodes[3];
        await context.WaitAsync("the sentinels to agree on the master", TimeSpan.FromSeconds(60),
            async () =>
            {
                var master = await context.ExecAsync(firstSentinel.ContainerName,
                    SentinelCli("sentinel", "get-master-addr-by-name", service), cancellationToken)
                    .ConfigureAwait(false);
                if (!master.Stdout.Contains(gateway, StringComparison.Ordinal)
                    || !master.Stdout.Contains(
                        dataPorts[0].ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    return false;
                }

                var peers = await context.ExecAsync(firstSentinel.ContainerName,
                    SentinelCli("sentinel", "sentinels", service), cancellationToken)
                    .ConfigureAwait(false);
                var replicas = await context.ExecAsync(firstSentinel.ContainerName,
                    SentinelCli("sentinel", "replicas", service), cancellationToken)
                    .ConfigureAwait(false);

                return CountOccurrences(peers.Stdout, "runid") == 2
                    && CountOccurrences(replicas.Stdout, "runid") == 2;
            }, cancellationToken).ConfigureAwait(false);

        var endpoints = new List<RedisEndpoint>();
        for (var index = 0; index < sentinelPorts.Count; index++)
        {
            endpoints.Add(new RedisEndpoint
            {
                Host = "127.0.0.1",
                Port = sentinelPorts[index],
                Role = NodeRole.Sentinel,
                NodeIndex = 4 + index,
                IsSentinel = true,
            });
        }

        endpoints.Add(new RedisEndpoint
        {
            Host = "127.0.0.1", Port = dataPorts[0], Role = NodeRole.Primary, NodeIndex = 1,
        });
        for (var index = 1; index < dataPorts.Count; index++)
        {
            endpoints.Add(new RedisEndpoint
            {
                Host = "127.0.0.1",
                Port = dataPorts[index],
                Role = NodeRole.Replica,
                NodeIndex = index + 1,
            });
        }

        return new TopologyBuildResult
        {
            Nodes = nodes,
            Connection = ConnectionInfoFactory.Build(ConnectionShape.Sentinel, endpoints, password,
                service, notes:
                [
                    "Connect with ConnectionMultiplexer.SentinelConnect, then GetSentinelMasterConnection.",
                    "The sentinels monitor and report " + gateway + ":" + dataPorts[0]
                        + ", which is reachable from the host and from other containers.",
                    "Quorum is 2 of 3.",
                ]),
        };
    }

    /// <summary>Builds the shell script the sentinel container runs.</summary>
    /// <param name="gateway">The network gateway.</param>
    /// <param name="sentinelHostPort">The sentinel's own published port.</param>
    /// <param name="primaryHostPort">The primary's published port.</param>
    /// <param name="password">The data-node password.</param>
    /// <param name="service">The master name.</param>
    /// <returns>A single shell command that writes the config and execs the sentinel.</returns>
    internal static string SentinelScript(string gateway, int sentinelHostPort, int primaryHostPort,
        string password, string service)
    {
        var config = new StringBuilder();
        config.Append("port ").Append(SentinelContainerPort).Append("\\n");
        config.Append("sentinel announce-ip ").Append(gateway).Append("\\n");
        config.Append("sentinel announce-port ").Append(sentinelHostPort).Append("\\n");
        config.Append("sentinel monitor ").Append(service).Append(' ').Append(gateway).Append(' ')
            .Append(primaryHostPort).Append(" 2\\n");
        if (!string.IsNullOrEmpty(password))
        {
            config.Append("sentinel auth-pass ").Append(service).Append(' ').Append(password)
                .Append("\\n");
        }

        config.Append("sentinel down-after-milliseconds ").Append(service).Append(" 5000\\n");
        config.Append("sentinel failover-timeout ").Append(service).Append(" 10000\\n");
        config.Append("sentinel parallel-syncs ").Append(service).Append(" 1\\n");

        return "printf " + ShellQuote(config.ToString())
            + " > /data/sentinel.conf && exec redis-sentinel /data/sentinel.conf";
    }

    /// <summary>Wraps a value in single quotes, escaping any single quote it contains.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A shell-safe literal.</returns>
    internal static string ShellQuote(string value) =>
        "'" + (value ?? string.Empty).Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string[] SentinelCli(params string[] arguments)
    {
        //Sentinels carry no password of their own, so this deliberately skips the -a flag.
        var command = new List<string>
        {
            "redis-cli", "-p", SentinelContainerPort.ToString(CultureInfo.InvariantCulture),
        };
        command.AddRange(arguments);
        return [.. command];
    }

    private static int CountOccurrences(string text, string token)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static IReadOnlyList<string> DataCommand(string password, string gateway, int announcePort,
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
