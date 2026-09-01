using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Redlock;
using RedisSetupTool.RedisManagement.Tests.Fakes;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Covers release, extend and disposal.</summary>
public class RedlockHandleTests
{
    /// <summary>Release runs the compare-and-set script on every node with the handle's token.</summary>
    [Fact]
    public async Task ReleaseAsync_RunsTheScriptOnEveryNodeWithTheOwnToken()
    {
        //Arrange
        var (handle, servers) = await AcquireAsync();

        //Act
        await handle.ReleaseAsync(TestContext.Current.CancellationToken);

        //Assert
        foreach (var server in servers)
        {
            server.ScriptCalls.Count.Should().Be(1);
            server.ScriptCalls[0].Script.Should().Be(RedlockHandle.ReleaseScript);
            server.ScriptCalls[0].Values[0].Should().Be(handle.Token);
            server.Data.ContainsKey("resource").Should().Be(false);
        }
    }

    /// <summary>A node whose value no longer matches the token is left alone.</summary>
    [Fact]
    public async Task ReleaseAsync_WhenTheTokenNoLongerMatches_RemovesNothing()
    {
        //Arrange
        var (handle, servers) = await AcquireAsync();
        await servers[0].StringSetAsync("resource", "somebody-else");

        //Act
        await handle.ReleaseAsync(TestContext.Current.CancellationToken);

        //Assert
        servers[0].Data["resource"].Should().Be("somebody-else");
    }

    /// <summary>Disposal releases once and is safe to repeat.</summary>
    [Fact]
    public async Task DisposeAsync_ReleasesOnceAndIsIdempotent()
    {
        //Arrange
        var (handle, servers) = await AcquireAsync();

        //Act
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        //Assert
        servers[0].ScriptCalls.Count.Should().Be(1);
        handle.IsHeld.Should().Be(false);
        servers[0].Disposed.Should().Be(true);
    }

    /// <summary>Extend fails when too few nodes still carry the token.</summary>
    [Fact]
    public async Task ExtendAsync_WhenQuorumIsLost_ReturnsFalse()
    {
        //Arrange
        var (handle, servers) = await AcquireAsync();
        for (var index = 0; index < 4; index++)
        {
            await servers[index].KeyDeleteAsync("resource");
        }

        //Act
        var extended = await handle.ExtendAsync(TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        //Assert
        extended.Should().Be(false);
        handle.IsHeld.Should().Be(false);
    }

    /// <summary>Extend succeeds while a quorum still carries the token.</summary>
    [Fact]
    public async Task ExtendAsync_WhenQuorumHolds_ReturnsTrue()
    {
        //Arrange
        var (handle, _) = await AcquireAsync();

        //Act
        var extended = await handle.ExtendAsync(TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        //Assert
        extended.Should().Be(true);
    }

    private static async Task<(RedlockHandle Handle, IReadOnlyList<FakeRedisServer> Servers)>
        AcquireAsync()
    {
        var factory = new FakeRedisConnectionFactory();
        var masters = new List<RedisHostPort>();
        var servers = new List<FakeRedisServer>();

        for (var index = 0; index < 5; index++)
        {
            var endpoint = new RedisHostPort { Host = "127.0.0.1", Port = 6401 + index };
            var server = new FakeRedisServer(endpoint.ToString());
            factory.With(endpoint.ToString(), server);
            masters.Add(endpoint);
            servers.Add(server);
        }

        var service = new RedlockService(factory);
        var handle = await service.AcquireAsync(masters, null, "resource", TimeSpan.FromSeconds(30),
            new RedlockOptions { NodeTimeout = TimeSpan.FromSeconds(5) });
        return (handle, servers);
    }
}
