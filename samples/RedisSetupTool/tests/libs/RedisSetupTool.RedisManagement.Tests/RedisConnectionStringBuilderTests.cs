using System;
using CodeBrix.Redis;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Covers the paste-ready text and the client options, per shape.</summary>
public class RedisConnectionStringBuilderTests
{
    /// <summary>A standalone deployment gets one endpoint and its password.</summary>
    [Fact]
    public void Build_ForStandalone_ProducesTheDocumentedString()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.Standalone, "secret",
            Endpoint("127.0.0.1", 6401));

        //Act
        var text = RedisConnectionStringBuilder.Build(descriptor);

        //Assert
        text.Should().Be("127.0.0.1:6401,password=secret,allowAdmin=True,abortConnect=False");
    }

    /// <summary>A primary and its replica are both listed.</summary>
    [Fact]
    public void Build_ForPrimaryReplica_ListsBothEndpoints()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.PrimaryReplica, "pw",
            Endpoint("127.0.0.1", 6401), Endpoint("127.0.0.1", 6402));

        //Act
        var text = RedisConnectionStringBuilder.Build(descriptor);

        //Assert
        text.Should().Be(
            "127.0.0.1:6401,127.0.0.1:6402,password=pw,allowAdmin=True,abortConnect=False");
    }

    /// <summary>A sentinel string names the service and lists only the sentinels.</summary>
    [Fact]
    public void Build_ForSentinel_NamesTheServiceAndOmitsTheDataNodes()
    {
        //Arrange
        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Sentinel,
            ServiceName = "mymaster",
            Credentials = new RedisCredentials { Password = "pw" },
            Endpoints =
            [
                Sentinel("127.0.0.1", 26401), Sentinel("127.0.0.1", 26402),
                Sentinel("127.0.0.1", 26403), Endpoint("127.0.0.1", 6401),
            ],
        };

        //Act
        var text = RedisConnectionStringBuilder.Build(descriptor);

        //Assert
        text.Should().Be("127.0.0.1:26401,127.0.0.1:26402,127.0.0.1:26403,"
            + "serviceName=mymaster,password=pw,abortConnect=False");
    }

    /// <summary>Cluster seeds use the gateway address the nodes advertise.</summary>
    [Fact]
    public void Build_ForCluster_UsesTheAdvertisedSeeds()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.Cluster, null,
            Endpoint("172.18.0.1", 7401), Endpoint("172.18.0.1", 7402));

        //Act
        var text = RedisConnectionStringBuilder.Build(descriptor);

        //Assert
        text.Should().Be("172.18.0.1:7401,172.18.0.1:7402,allowAdmin=True,abortConnect=False");
    }

    /// <summary>A quorum gets one line per master, because it is N connections.</summary>
    [Fact]
    public void Build_ForIndependentQuorum_ProducesOneLinePerMaster()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.IndependentQuorum, "pw",
            Endpoint("127.0.0.1", 6401), Endpoint("127.0.0.1", 6402));

        //Act
        var lines = RedisConnectionStringBuilder.Build(descriptor).Split('\n');

        //Assert
        lines.Length.Should().Be(2);
        lines[0].Should().Be("127.0.0.1:6401,password=pw,allowAdmin=True,abortConnect=False");
    }

    /// <summary>The sentinel options carry the service name and the sentinel command map.</summary>
    [Fact]
    public void BuildOptions_ForSentinel_SetsTheServiceNameAndCommandMap()
    {
        //Arrange
        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Sentinel,
            ServiceName = "mymaster",
            Endpoints = [Sentinel("127.0.0.1", 26401)],
        };

        //Act
        var options = RedisConnectionStringBuilder.BuildOptions(descriptor);

        //Assert
        options.ServiceName.Should().Be("mymaster");
        options.CommandMap.Should().Be(CommandMap.Sentinel);
        options.TieBreaker.Should().Be(string.Empty);
    }

    /// <summary>A quorum has no single options object, so asking for one is refused.</summary>
    [Fact]
    public void BuildOptions_ForIndependentQuorum_Throws()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.IndependentQuorum, "pw",
            Endpoint("127.0.0.1", 6401));

        //Act
        var act = () => RedisConnectionStringBuilder.BuildOptions(descriptor);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>A password adds the flag that silences the client's warning.</summary>
    [Fact]
    public void BuildCliCommand_WhenThereIsAPassword_AddsTheNoAuthWarningFlag()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.Standalone, "secret",
            Endpoint("127.0.0.1", 6401));

        //Act
        var command = RedisConnectionStringBuilder.BuildCliCommand(descriptor);

        //Assert
        command.Should().Be("redis-cli -h 127.0.0.1 -p 6401 -a secret --no-auth-warning");
    }

    /// <summary>A cluster invocation follows redirects.</summary>
    [Fact]
    public void BuildCliCommand_ForCluster_AddsTheRedirectFlag()
    {
        //Arrange
        var descriptor = Descriptor(RedisConnectionShape.Cluster, null,
            Endpoint("172.18.0.1", 7401));

        //Act
        var command = RedisConnectionStringBuilder.BuildCliCommand(descriptor);

        //Assert
        command.Should().Be("redis-cli -c -h 172.18.0.1 -p 7401");
    }

    private static RedisHostPort Endpoint(string host, int port) =>
        new() { Host = host, Port = port };

    private static RedisHostPort Sentinel(string host, int port) =>
        new() { Host = host, Port = port, IsSentinel = true };

    private static RedisConnectionDescriptor Descriptor(RedisConnectionShape shape, string password,
        params RedisHostPort[] endpoints) =>
        new()
        {
            Shape = shape,
            Endpoints = endpoints,
            Credentials = password is null ? null : new RedisCredentials { Password = password },
        };
}
