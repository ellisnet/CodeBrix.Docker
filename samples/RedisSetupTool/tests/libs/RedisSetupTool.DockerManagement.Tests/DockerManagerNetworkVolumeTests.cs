using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Network and volume operations against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class DockerManagerNetworkVolumeTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public DockerManagerNetworkVolumeTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// A created network reports a gateway address. Every announce-based topology depends on that
    /// address, so this is the check that matters most in this file.
    /// </summary>
    [Fact]
    public async Task CreateNetworkAsync_ReportsAGatewayAddress()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("net");
        var token = TestContext.Current.CancellationToken;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RedisSetupToolFixture.TestLabelName] = RedisSetupToolFixture.TestLabelValue,
            [InstanceLabels.Instance] = "a1-netcheck",
        };

        try
        {
            //Act
            var id = await _fixture.Docker.CreateNetworkAsync(name, labels, token);
            var network = await _fixture.Docker.InspectNetworkAsync(name, token);

            //Assert
            id.Should().NotBeNullOrEmpty();
            network.Name.Should().Be(name);
            network.Driver.Should().Be("bridge");
            network.Gateway.Should().NotBeNullOrEmpty();
            network.Subnet.Should().NotBeNullOrEmpty();
            network.InstanceId.Should().Be("a1-netcheck");
        }
        finally
        {
            DockerCli.TryRun("network", "rm", name);
        }
    }

    /// <summary>A container can be attached to a network with an alias and detached again.</summary>
    [Fact]
    public async Task ConnectAndDisconnect_AttachTheContainerWithAnAlias()
    {
        //Arrange
        var network = RedisSetupToolFixture.NewName("net");
        var container = RedisSetupToolFixture.NewName("attached");
        var token = TestContext.Current.CancellationToken;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RedisSetupToolFixture.TestLabelName] = RedisSetupToolFixture.TestLabelValue,
        };

        try
        {
            await _fixture.Docker.CreateNetworkAsync(network, labels, token);
            DockerCli.Run("run", "-d", "--name", container,
                "--label", RedisSetupToolFixture.TestLabelName + "="
                    + RedisSetupToolFixture.TestLabelValue,
                "alpine:latest", "sleep", "300");

            //Act
            await _fixture.Docker.ConnectContainerAsync(network, container, ["primary"], token);
            var attached = await _fixture.Docker.InspectNetworkAsync(network, token);
            var detail = await _fixture.Docker.InspectContainerAsync(container, token);

            await _fixture.Docker.DisconnectContainerAsync(network, container, true, token);
            var detached = await _fixture.Docker.InspectNetworkAsync(network, token);

            //Assert
            attached.AttachedContainerCount.Should().Be(1);
            attached.AttachedContainers[0].ContainerName.Should().Be(container);

            var aliases = new List<string>();
            foreach (var attachment in detail.Networks)
            {
                if (attachment.NetworkName == network)
                {
                    foreach (var alias in attachment.Aliases)
                    {
                        aliases.Add(alias);
                    }
                }
            }

            aliases.Should().Contain("primary");
            detached.AttachedContainerCount.Should().Be(0);
        }
        finally
        {
            DockerCli.RemoveQuietly(container);
            DockerCli.TryRun("network", "rm", network);
        }
    }

    /// <summary>A labelled volume can be created, inspected and removed.</summary>
    [Fact]
    public async Task Volumes_CanBeCreatedInspectedAndRemoved()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("vol");
        var token = TestContext.Current.CancellationToken;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RedisSetupToolFixture.TestLabelName] = RedisSetupToolFixture.TestLabelValue,
            [InstanceLabels.Instance] = "a1-volcheck",
        };

        try
        {
            //Act
            var created = await _fixture.Docker.CreateVolumeAsync(name, labels, token);
            var volume = await _fixture.Docker.InspectVolumeAsync(name, token);
            var listed = await _fixture.Docker.ListVolumesAsync(token);

            await _fixture.Docker.RemoveVolumeAsync(name, true, token);
            var act = () => _fixture.Docker.InspectVolumeAsync(name, token);

            //Assert
            created.Should().Be(name);
            volume.Driver.Should().Be("local");
            volume.Mountpoint.Should().NotBeNullOrEmpty();
            volume.InstanceId.Should().Be("a1-volcheck");

            var names = new List<string>();
            foreach (var entry in listed)
            {
                names.Add(entry.Name);
            }

            names.Should().Contain(name);
            var thrown = await act.Should().ThrowAsync<DockerManagementException>();
            thrown.And.IsNotFound.Should().Be(true);
        }
        finally
        {
            DockerCli.TryRun("volume", "rm", "-f", name);
        }
    }
}
