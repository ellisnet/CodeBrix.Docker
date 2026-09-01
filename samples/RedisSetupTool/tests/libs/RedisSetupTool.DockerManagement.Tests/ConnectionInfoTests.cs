using System.Collections.Generic;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers the paste-ready connection text for each of the five shapes.</summary>
public class ConnectionInfoTests
{
    /// <summary>A standalone instance produces one endpoint and its password.</summary>
    [Fact]
    public void Build_ForStandalone_ProducesTheDocumentedString()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.Standalone,
            [Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1)], "pw");

        //Assert
        info.ConnectionString.Should()
            .Be("127.0.0.1:6401,password=pw,allowAdmin=True,abortConnect=False");
        info.CliCommand.Should().Be("redis-cli -h 127.0.0.1 -p 6401 -a pw --no-auth-warning");
        info.Username.Should().Be("default");
    }

    /// <summary>Without a password the string carries no password clause.</summary>
    [Fact]
    public void Build_WithNoPassword_OmitsThePasswordClause()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.Standalone,
            [Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1)], null);

        //Assert
        info.ConnectionString.Should().Be("127.0.0.1:6401,allowAdmin=True,abortConnect=False");
        info.CliCommand.Should().Be("redis-cli -h 127.0.0.1 -p 6401");
    }

    /// <summary>A primary and replica are both listed, primary first.</summary>
    [Fact]
    public void Build_ForPrimaryReplica_ListsBothEndpoints()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.PrimaryReplica,
        [
            Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1),
            Endpoint("127.0.0.1", 6402, NodeRole.Replica, 2),
        ], "pw");

        //Assert
        info.ConnectionString.Should()
            .Be("127.0.0.1:6401,127.0.0.1:6402,password=pw,allowAdmin=True,abortConnect=False");
    }

    /// <summary>A sentinel string lists the sentinels and names the service.</summary>
    [Fact]
    public void Build_ForSentinel_ListsSentinelsAndNamesTheService()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.Sentinel,
        [
            Sentinel("127.0.0.1", 26401, 4),
            Sentinel("127.0.0.1", 26402, 5),
            Sentinel("127.0.0.1", 26403, 6),
            Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1),
        ], "pw", "mymaster");

        //Assert
        info.ConnectionString.Should().Be("127.0.0.1:26401,127.0.0.1:26402,127.0.0.1:26403,"
            + "serviceName=mymaster,password=pw,abortConnect=False");
        info.ServiceName.Should().Be("mymaster");
        info.CliCommand.Should().Be("redis-cli -h 127.0.0.1 -p 26401");
    }

    /// <summary>Cluster seeds use the gateway address the nodes advertise.</summary>
    [Fact]
    public void Build_ForCluster_UsesTheGatewaySeeds()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.Cluster,
        [
            Endpoint("172.18.0.1", 7401, NodeRole.ClusterPrimary, 1),
            Endpoint("172.18.0.1", 7402, NodeRole.ClusterPrimary, 2),
            Endpoint("172.18.0.1", 7403, NodeRole.ClusterPrimary, 3),
        ], null);

        //Assert
        info.ConnectionString.Should().Be(
            "172.18.0.1:7401,172.18.0.1:7402,172.18.0.1:7403,allowAdmin=True,abortConnect=False");
        info.CliCommand.Should().Be("redis-cli -c -h 172.18.0.1 -p 7401");
    }

    /// <summary>A quorum produces one line per master plus its notes.</summary>
    [Fact]
    public void Build_ForIndependentQuorum_ProducesOneLinePerMaster()
    {
        //Arrange
        IReadOnlyList<string> notes = ["Quorum is 3 of 5."];

        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.IndependentQuorum,
        [
            Endpoint("127.0.0.1", 6401, NodeRole.QuorumMaster, 1),
            Endpoint("127.0.0.1", 6402, NodeRole.QuorumMaster, 2),
        ], "pw", notes: notes);

        //Act
        var lines = info.ConnectionString.Split('\n');

        //Assert
        lines.Length.Should().Be(2);
        lines[1].Should().Be("127.0.0.1:6402,password=pw,allowAdmin=True,abortConnect=False");
        info.Notes[0].Should().Be("Quorum is 3 of 5.");
    }

    /// <summary>The Valkey preset suggests its own command-line client.</summary>
    [Fact]
    public void Build_ForValkey_SuggestsTheValkeyClient()
    {
        //Act
        var info = ConnectionInfoFactory.Build(ConnectionShape.Standalone,
            [Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1)], null,
            cliExecutable: ConnectionInfoFactory.ValkeyCli);

        //Assert
        info.CliCommand.Should().Be("valkey-cli -h 127.0.0.1 -p 6401");
    }

    /// <summary>An endpoint renders host and port.</summary>
    [Fact]
    public void RedisEndpoint_ToString_RendersHostAndPort()
    {
        //Assert
        Endpoint("127.0.0.1", 6401, NodeRole.Primary, 1).ToString().Should().Be("127.0.0.1:6401");
    }

    private static RedisEndpoint Endpoint(string host, int port, NodeRole role, int index) =>
        new() { Host = host, Port = port, Role = role, NodeIndex = index };

    private static RedisEndpoint Sentinel(string host, int port, int index) =>
        new() { Host = host, Port = port, Role = NodeRole.Sentinel, NodeIndex = index, IsSentinel = true };
}
