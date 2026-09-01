using System;
using System.Globalization;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>B1, the two-node replication topology, against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class TopologyReplicaTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public TopologyReplicaTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The replica attaches through the gateway and receives writes.</summary>
    [Fact]
    public async Task B1_ReplicatesFromThePrimaryThroughTheGateway()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.B1);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var primary = created.Nodes[0];
            var replica = created.Nodes[1];
            var password = created.Connection.Password;

            var onPrimary = await _fixture.Docker.RunCommandAsync(primary.ContainerName,
                Cli(password, "INFO", "replication"), cancellationToken: token);
            var onReplica = await _fixture.Docker.RunCommandAsync(replica.ContainerName,
                Cli(password, "INFO", "replication"), cancellationToken: token);

            await _fixture.Docker.RunCommandAsync(primary.ContainerName,
                Cli(password, "SET", "replicated-key", "value"), cancellationToken: token);
            await Task.Delay(600, token);
            var read = await _fixture.Docker.RunCommandAsync(replica.ContainerName,
                Cli(password, "GET", "replicated-key"), cancellationToken: token);

            //Assert
            created.Nodes.Count.Should().Be(2);
            primary.Role.Should().Be(NodeRole.Primary);
            replica.Role.Should().Be(NodeRole.Replica);
            created.Connection.Shape.Should().Be(ConnectionShape.PrimaryReplica);
            created.Connection.Endpoints.Count.Should().Be(2);

            onPrimary.Stdout.Should().Contain("connected_slaves:1");
            //The replica announces itself at the gateway, so the primary's view is host-reachable.
            onPrimary.Stdout.Should().Contain(created.AnnounceIp);
            onPrimary.Stdout.Should().Contain("port="
                + replica.HostPort.ToString(CultureInfo.InvariantCulture));
            onReplica.Stdout.Should().Contain("master_link_status:up");
            read.Stdout.Should().Contain("value");

            var discovered = await _fixture.Topologies.RefreshAsync(instanceId, token);
            discovered.Nodes.Count.Should().Be(2);
            discovered.Nodes[1].Role.Should().Be(NodeRole.Replica);
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    private static string[] Cli(string password, params string[] arguments)
    {
        var command = new System.Collections.Generic.List<string>
        {
            "redis-cli", "-p", "6379",
        };

        if (!string.IsNullOrEmpty(password))
        {
            command.Add("-a");
            command.Add(password);
            command.Add("--no-auth-warning");
        }

        command.AddRange(arguments);
        return [.. command];
    }
}
