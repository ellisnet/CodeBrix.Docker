using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>The single-container topologies, created and torn down against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class TopologyStandaloneTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public TopologyStandaloneTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>A1 is created, rediscovered from its labels alone, and destroyed completely.</summary>
    [Fact]
    public async Task A1_CreateDiscoverDestroy_LeavesNothingBehind()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A1);
        var steps = new List<string>();
        var progress = new Progress<TopologyProgress>(step => steps.Add(step.Message));
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, progress, token);
            instanceId = created.InstanceId;

            var discovered = await _fixture.Topologies.RefreshAsync(instanceId, token);
            var ping = await _fixture.Docker.RunCommandAsync(created.Nodes[0].ContainerName,
                ["redis-cli", "-p", "6379", "ping"], cancellationToken: token);

            //Assert
            created.State.Should().Be(InstanceState.Running);
            created.Nodes.Count.Should().Be(1);
            created.Nodes[0].HostPort.Should().BeGreaterThan(6399);
            created.Connection.Shape.Should().Be(ConnectionShape.Standalone);
            created.Connection.ConnectionString.Should().Contain("127.0.0.1:"
                + created.Nodes[0].HostPort.ToString(CultureInfo.InvariantCulture));
            created.AnnounceIp.Should().NotBeNullOrEmpty();
            steps.Count.Should().BeGreaterThan(2);

            discovered.Should().NotBeNull();
            discovered.InstanceId.Should().Be(created.InstanceId);
            discovered.TopologyId.Should().Be(TopologyId.A1);
            discovered.InstanceName.Should().Be(created.InstanceName);
            discovered.Nodes.Count.Should().Be(1);
            discovered.Nodes[0].HostPort.Should().Be(created.Nodes[0].HostPort);
            discovered.State.Should().Be(InstanceState.Running);
            discovered.VolumeNames.Count.Should().Be(1);

            ping.Stdout.Should().Contain("PONG");
        }
        finally
        {
            if (instanceId is not null)
            {
                await _fixture.Topologies.DestroyAsync(instanceId, null, token);
            }
        }

        //Assert - nothing carrying the instance label is left
        var containers = await _fixture.Docker.ListInstanceContainersAsync(instanceId, token);
        containers.Count.Should().Be(0);

        foreach (var volume in await _fixture.Docker.ListVolumesAsync(token))
        {
            volume.InstanceId.Should().NotBe(instanceId);
        }

        foreach (var network in await _fixture.Docker.ListNetworksAsync(token))
        {
            network.InstanceId.Should().NotBe(instanceId);
        }
    }

    /// <summary>A2 requires the password: an unauthenticated command is refused.</summary>
    [Fact]
    public async Task A2_RequiresThePassword()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A2);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;
            var node = created.Nodes[0].ContainerName;

            var unauthenticated = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "get", "anything"], cancellationToken: token);
            var authenticated = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "-a", created.Connection.Password, "--no-auth-warning",
                    "ping"], cancellationToken: token);

            //Assert
            created.Connection.Password.Should().StartWith("redis-");
            created.Connection.ConnectionString.Should().Contain("password="
                + created.Connection.Password);
            unauthenticated.Stdout.Should().Contain("NOAUTH");
            authenticated.Stdout.Should().Contain("PONG");

            //The password survives a rediscovery, which is what makes the card reopenable.
            var discovered = await _fixture.Topologies.RefreshAsync(instanceId, token);
            discovered.Connection.Password.Should().Be(created.Connection.Password);
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>A3 declares its extra users on the command line, with no ACL file.</summary>
    [Fact]
    public async Task A3_DeclaresItsAclUsers()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A3);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var acl = await _fixture.Docker.RunCommandAsync(created.Nodes[0].ContainerName,
                ["redis-cli", "-p", "6379", "-a", created.Connection.Password, "--no-auth-warning",
                    "ACL", "LIST"], cancellationToken: token);

            //Assert
            created.Connection.AdditionalUsers.Count.Should().Be(2);
            acl.Stdout.Should().Contain("user app");
            acl.Stdout.Should().Contain("user readonly");

            var discovered = await _fixture.Topologies.RefreshAsync(instanceId, token);
            discovered.Connection.AdditionalUsers.Count.Should().Be(2);
            discovered.Connection.AdditionalUsers[0].Username.Should().Be("app");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>A5 turns both persistence mechanisms on.</summary>
    [Fact]
    public async Task A5_EnablesBothPersistenceMechanisms()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A5);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;
            var node = created.Nodes[0].ContainerName;

            var appendonly = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "CONFIG", "GET", "appendonly"], cancellationToken: token);
            var save = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "CONFIG", "GET", "save"], cancellationToken: token);

            //Assert
            appendonly.Stdout.Should().Contain("yes");
            //A multi-token save policy passed as one argument is accepted verbatim.
            save.Stdout.Should().Contain("900 1 300 10 60 10000");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>A6 applies the memory cap and the eviction policy.</summary>
    [Fact]
    public async Task A6_AppliesTheCapAndThePolicy()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A6);
        request.Parameters["policy"] = "allkeys-lfu";
        request.Parameters["maxmemory"] = "32mb";
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;
            var node = created.Nodes[0].ContainerName;

            var policy = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "CONFIG", "GET", "maxmemory-policy"],
                cancellationToken: token);
            var cap = await _fixture.Docker.RunCommandAsync(node,
                ["redis-cli", "-p", "6379", "CONFIG", "GET", "maxmemory"], cancellationToken: token);

            //Assert
            policy.Stdout.Should().Contain("allkeys-lfu");
            cap.Stdout.Should().Contain((32L * 1024 * 1024).ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>
    /// F3 must find all five bundled modules. They load only because the image's own entrypoint is
    /// left alone; overriding it would leave the node answering PING with four modules missing.
    /// </summary>
    [Fact]
    public async Task F3_LoadsAllFiveBundledModules()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.F3);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var modules = await _fixture.Docker.RunCommandAsync(created.Nodes[0].ContainerName,
                ["redis-cli", "-p", "6379", "MODULE", "LIST"], cancellationToken: token);

            //Assert
            modules.Stdout.Should().Contain("search");
            modules.Stdout.Should().Contain("bf");
            modules.Stdout.Should().Contain("vectorset");
            modules.Stdout.Should().Contain("timeseries");
            modules.Stdout.Should().Contain("ReJSON");

            var detail = await _fixture.Docker.InspectContainerAsync(created.Nodes[0].ContainerName,
                token);
            detail.Entrypoint.Should().Contain("docker-entrypoint.sh");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>G1 applies real container limits, with swap disabled by matching them.</summary>
    [Fact]
    public async Task G1_AppliesContainerLimitsWithSwapDisabled()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.G1);
        request.Parameters["containerMemoryMb"] = "96";
        request.Parameters["maxmemoryMb"] = "64";
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var detail = await _fixture.Docker.InspectContainerAsync(created.Nodes[0].ContainerName,
                token);
            var findings = await _fixture.Docker.AdviseContainerAsync(
                created.Nodes[0].ContainerName, token);

            //Assert
            detail.Limits.MemoryBytes.Should().Be(96L * 1024 * 1024);
            detail.Limits.IsSwapDisabled.Should().Be(true);
            detail.Limits.HasCpuLimit.Should().Be(true);
            detail.Limits.PidsLimit.Should().Be(128);

            //The advisor should stop complaining about the limits and start on the healthcheck.
            var codes = new List<string>();
            foreach (var finding in findings)
            {
                codes.Add(finding.RuleId);
            }

            codes.Should().Contain("CB007");
            codes.Should().NotContain("CB001");
            codes.Should().NotContain("CB002");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>E4 runs on Valkey, whose image ships the Redis client binaries too.</summary>
    [Fact]
    public async Task E4_RunsOnValkey()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        await _fixture.Docker.PullImageAsync(TopologyCatalog.ValkeyImage, null, token);
        var request = RedisSetupToolFixture.Request(TopologyId.E4);
        string instanceId = null;

        try
        {
            //Act
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            var info = await _fixture.Docker.RunCommandAsync(created.Nodes[0].ContainerName,
                ["redis-cli", "-p", "6379", "INFO", "server"], cancellationToken: token);

            //Assert
            created.Image.Should().Be("valkey/valkey:8.1-alpine");
            created.Connection.CliCommand.Should().StartWith("valkey-cli");
            info.Stdout.Should().Contain("valkey_version");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>Stopping and starting an instance walks its containers in dependency order.</summary>
    [Fact]
    public async Task StopAndStart_TakeTheInstanceDownAndBringItBack()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        var request = RedisSetupToolFixture.Request(TopologyId.A1);
        string instanceId = null;

        try
        {
            var created = await _fixture.Topologies.CreateAsync(request, null, token);
            instanceId = created.InstanceId;

            //Act
            await _fixture.Topologies.StopAsync(instanceId, token);
            var stopped = await _fixture.Topologies.RefreshAsync(instanceId, token);

            await _fixture.Topologies.StartAsync(instanceId, token);
            var restarted = await _fixture.Topologies.RefreshAsync(instanceId, token);

            //Assert
            stopped.State.Should().Be(InstanceState.Stopped);
            stopped.StatusText.Should().Be("0 of 1 running");
            restarted.State.Should().Be(InstanceState.Running);
            restarted.StatusText.Should().Be("1 of 1 running");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(instanceId);
        }
    }

    /// <summary>Discovery finds every instance on the daemon and groups them correctly.</summary>
    [Fact]
    public async Task DiscoverAsync_FindsEveryInstance()
    {
        //Arrange
        var token = TestContext.Current.CancellationToken;
        string first = null;
        string second = null;

        try
        {
            var one = await _fixture.Topologies.CreateAsync(
                RedisSetupToolFixture.Request(TopologyId.A1, "discovery-one"), null, token);
            first = one.InstanceId;
            var two = await _fixture.Topologies.CreateAsync(
                RedisSetupToolFixture.Request(TopologyId.A2, "discovery-two"), null, token);
            second = two.InstanceId;

            //Act
            var discovered = await _fixture.Topologies.DiscoverAsync(token);

            //Assert
            var names = new List<string>();
            foreach (var instance in discovered)
            {
                names.Add(instance.InstanceName);
                instance.Nodes.Count.Should().BeGreaterThan(0);
            }

            names.Should().Contain("discovery-one");
            names.Should().Contain("discovery-two");
        }
        finally
        {
            await _fixture.DestroyQuietlyAsync(first);
            await _fixture.DestroyQuietlyAsync(second);
        }
    }
}
