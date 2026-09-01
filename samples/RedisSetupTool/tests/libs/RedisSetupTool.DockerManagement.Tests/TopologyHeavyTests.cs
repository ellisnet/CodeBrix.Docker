using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>
/// The six-container topologies. They are gated because C1 and D2 together are three minutes of
/// container churn, and an ordinary pass should stay quick.
/// </summary>
[Collection(RedisSetupToolCollection.Name)]
public class TopologyHeavyTests
{
    /// <summary>The environment variable that opens the gate.</summary>
    public const string Gate = "REDISSETUP_TEST_HEAVY";

    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public TopologyHeavyTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// C1's sentinels must hand out an address the host can reach. Monitoring by container alias
    /// would work inside the network and be useless to any client outside it.
    /// </summary>
    [EnvGatedFact(Gate)]
    public async Task C1_SentinelsReportTheGatewayAddressOfThePrimary()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.C1);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var sentinel = created.Nodes[3];
            var primaryPort = created.Nodes[0].HostPort.ToString(CultureInfo.InvariantCulture);

            var address = await _fixture.Docker.RunCommandAsync(sentinel.ContainerName,
                ["redis-cli", "-p", "26379", "sentinel", "get-master-addr-by-name", "mymaster"],
                cancellationToken: token);
            var replicas = await _fixture.Docker.RunCommandAsync(sentinel.ContainerName,
                ["redis-cli", "-p", "26379", "sentinel", "replicas", "mymaster"],
                cancellationToken: token);
            var peers = await _fixture.Docker.RunCommandAsync(sentinel.ContainerName,
                ["redis-cli", "-p", "26379", "sentinel", "sentinels", "mymaster"],
                cancellationToken: token);

            //Assert
            created.Nodes.Count.Should().Be(6);
            created.Connection.Shape.Should().Be(ConnectionShape.Sentinel);
            created.Connection.ServiceName.Should().Be("mymaster");

            address.Stdout.Should().Contain(created.AnnounceIp);
            address.Stdout.Should().Contain(primaryPort);

            //Both replicas announce themselves at the gateway and their own published ports.
            replicas.Stdout.Should().Contain(created.Nodes[1].HostPort
                .ToString(CultureInfo.InvariantCulture));
            replicas.Stdout.Should().Contain(created.Nodes[2].HostPort
                .ToString(CultureInfo.InvariantCulture));
            Count(peers.Stdout, "runid").Should().Be(2);
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>D2 reaches cluster_state:ok with all slots assigned and three primaries.</summary>
    [EnvGatedFact(Gate)]
    public async Task D2_ReachesClusterStateOkWithThreePrimaries()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.D2);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var node = created.Nodes[0];
            var info = await _fixture.Docker.RunCommandAsync(node.ContainerName,
                ["redis-cli", "-p", node.ContainerPort.ToString(CultureInfo.InvariantCulture),
                    "cluster", "info"], cancellationToken: token);
            var write = await _fixture.Docker.RunCommandAsync(node.ContainerName,
                ["redis-cli", "-c", "-h", created.AnnounceIp, "-p",
                    node.HostPort.ToString(CultureInfo.InvariantCulture), "set", "cluster-key",
                    "value"], cancellationToken: token);

            //Assert
            created.Nodes.Count.Should().Be(6);
            //Container port equals host port for this topology and no other.
            created.Nodes[0].ContainerPort.Should().Be(created.Nodes[0].HostPort);
            created.Nodes[0].BusHostPort.Should().Be(created.Nodes[0].HostPort + 10000);

            info.Stdout.Should().Contain("cluster_state:ok");
            info.Stdout.Should().Contain("cluster_slots_assigned:16384");
            info.Stdout.Should().Contain("cluster_known_nodes:6");
            write.Stdout.Should().Contain("OK");

            var primaries = 0;
            var replicas = 0;
            foreach (var member in created.Nodes)
            {
                if (member.Role == NodeRole.ClusterPrimary)
                {
                    primaries++;
                }
                else if (member.Role == NodeRole.ClusterReplica)
                {
                    replicas++;
                }
            }

            primaries.Should().Be(3);
            replicas.Should().Be(3);
            created.Connection.Endpoints[0].Host.Should().Be(created.AnnounceIp);

            var slotNotes = 0;
            foreach (var note in created.Connection.Notes)
            {
                if (note.Contains("slots", StringComparison.Ordinal))
                {
                    slotNotes++;
                }
            }

            slotNotes.Should().Be(3);
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>H1's five masters are independent and durable.</summary>
    [EnvGatedFact(Gate)]
    public async Task H1_CreatesFiveIndependentDurableMasters()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.H1);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            //Assert
            created.Nodes.Count.Should().Be(5);
            created.Connection.Shape.Should().Be(ConnectionShape.IndependentQuorum);
            created.Connection.ConnectionString.Split('\n').Length.Should().Be(5);

            foreach (var node in created.Nodes)
            {
                node.Role.Should().Be(NodeRole.QuorumMaster);

                var replication = await _fixture.Docker.RunCommandAsync(node.ContainerName,
                    ["redis-cli", "-p", "6379", "-a", created.Connection.Password,
                        "--no-auth-warning", "INFO", "replication"], cancellationToken: token);
                var fsync = await _fixture.Docker.RunCommandAsync(node.ContainerName,
                    ["redis-cli", "-p", "6379", "-a", created.Connection.Password,
                        "--no-auth-warning", "CONFIG", "GET", "appendfsync"],
                    cancellationToken: token);

                replication.Stdout.Should().Contain("role:master");
                replication.Stdout.Should().Contain("connected_slaves:0");
                fsync.Stdout.Should().Contain("always");
            }

            var notes = new List<string>(created.Connection.Notes);
            notes.Should().Contain("Quorum is 3 of 5.");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
