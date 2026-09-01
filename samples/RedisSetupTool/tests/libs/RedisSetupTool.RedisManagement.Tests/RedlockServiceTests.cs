using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Redlock;
using RedisSetupTool.RedisManagement.Tests.Fakes;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Covers the quorum arithmetic and the drift accounting.</summary>
public class RedlockServiceTests
{
    /// <summary>Five of five is held, and the quorum is three.</summary>
    [Fact]
    public async Task AcquireAsync_WhenEveryMasterGrants_IsHeldWithQuorumThree()
    {
        //Arrange
        var factory = Cluster(out var masters, out _);
        var service = new RedlockService(factory);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource",
            TimeSpan.FromSeconds(30), Options(),
            TestContext.Current.CancellationToken);

        //Assert
        handle.IsHeld.Should().Be(true);
        handle.AcquiredCount.Should().Be(5);
        handle.Quorum.Should().Be(3);
    }

    /// <summary>Three of five is still a quorum.</summary>
    [Fact]
    public async Task AcquireAsync_WhenThreeOfFiveGrant_IsHeld()
    {
        //Arrange
        var factory = Cluster(out var masters, out var servers);
        servers[3].RefuseSetNotExists = true;
        servers[4].RefuseSetNotExists = true;
        var service = new RedlockService(factory);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource",
            TimeSpan.FromSeconds(30), Options(), TestContext.Current.CancellationToken);

        //Assert
        handle.IsHeld.Should().Be(true);
        handle.AcquiredCount.Should().Be(3);
    }

    /// <summary>Two of five is not a quorum, and the two that granted are released.</summary>
    [Fact]
    public async Task AcquireAsync_WhenOnlyTwoGrant_IsNotHeldAndReleasesTheGrants()
    {
        //Arrange
        var factory = Cluster(out var masters, out var servers);
        for (var index = 2; index < 5; index++)
        {
            servers[index].RefuseSetNotExists = true;
        }

        var service = new RedlockService(factory);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource",
            TimeSpan.FromSeconds(30), Options(), TestContext.Current.CancellationToken);

        //Assert
        handle.IsHeld.Should().Be(false);
        handle.AcquiredCount.Should().Be(2);
        servers[0].Data.ContainsKey("resource").Should().Be(false);
        servers[1].Data.ContainsKey("resource").Should().Be(false);
    }

    /// <summary>A master that throws counts as a failure and does not abort the round.</summary>
    [Fact]
    public async Task AcquireAsync_WhenAMasterThrows_CountsAsAFailureWithoutAborting()
    {
        //Arrange
        var factory = Cluster(out var masters, out var servers);
        servers[0].Failure = new InvalidOperationException("node down");
        var service = new RedlockService(factory);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource",
            TimeSpan.FromSeconds(30), Options(), TestContext.Current.CancellationToken);

        //Assert
        handle.IsHeld.Should().Be(true);
        handle.AcquiredCount.Should().Be(4);
        handle.Acquisitions[0].ErrorMessage.Should().Contain("node down");
    }

    /// <summary>Validity is the lifetime less the elapsed time and the drift allowance.</summary>
    [Fact]
    public async Task AcquireAsync_ShrinksValidityByElapsedTimeAndDrift()
    {
        //Arrange
        var factory = Cluster(out var masters, out _);
        var service = new RedlockService(factory);
        var ttl = TimeSpan.FromSeconds(10);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource", ttl,
            Options(), TestContext.Current.CancellationToken);

        //Assert
        handle.Validity.Should().BeLessThan(ttl - TimeSpan.FromMilliseconds(99));
        handle.Validity.Should().BeGreaterThan(TimeSpan.FromSeconds(9));
    }

    /// <summary>A lifetime consumed entirely by latency is not a held lock, even at five of five.</summary>
    [Fact]
    public async Task AcquireAsync_WhenLatencyConsumesTheLifetime_IsNotHeld()
    {
        //Arrange
        var factory = Cluster(out var masters, out var servers);
        foreach (var server in servers)
        {
            server.Latency = TimeSpan.FromMilliseconds(120);
        }

        var options = Options();
        options.RetryCount = 1;
        var service = new RedlockService(factory);

        //Act
        await using var handle = await service.AcquireAsync(masters, null, "resource",
            TimeSpan.FromMilliseconds(100), options, TestContext.Current.CancellationToken);

        //Assert
        handle.AcquiredCount.Should().Be(5);
        handle.IsHeld.Should().Be(false);
    }

    private static RedlockOptions Options() => new()
    {
        RetryCount = 2,
        RetryDelay = TimeSpan.FromMilliseconds(10),
        //The algorithm's own rule is fifty milliseconds; the fakes need room for their latency.
        NodeTimeout = TimeSpan.FromSeconds(5),
    };

    private static FakeRedisConnectionFactory Cluster(out IReadOnlyList<RedisHostPort> masters,
        out IReadOnlyList<FakeRedisServer> servers)
    {
        var factory = new FakeRedisConnectionFactory();
        var endpoints = new List<RedisHostPort>();
        var nodes = new List<FakeRedisServer>();

        for (var index = 0; index < 5; index++)
        {
            var endpoint = new RedisHostPort { Host = "127.0.0.1", Port = 6401 + index };
            var server = new FakeRedisServer(endpoint.ToString());
            factory.With(endpoint.ToString(), server);
            endpoints.Add(endpoint);
            nodes.Add(server);
        }

        masters = endpoints;
        servers = nodes;
        return factory;
    }
}
