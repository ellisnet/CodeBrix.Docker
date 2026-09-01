using System;
using System.Collections.Generic;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Mapping;
using RedisSetupTool.DockerManagement.Models;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers the parts of the container mapping that need no daemon.</summary>
public class ContainerMapperTests
{
    /// <summary>The tool's labels project onto the container DTO.</summary>
    [Fact]
    public void ReadManagedLabels_ProjectsTheToolsLabels()
    {
        //Arrange
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InstanceLabels.Instance] = "d2-1a2b3c4d",
            [InstanceLabels.Topology] = "D2",
            [InstanceLabels.Role] = "cluster-primary",
            [InstanceLabels.Node] = "3",
        };

        //Act
        var managed = ContainerMapper.ReadManagedLabels(labels);

        //Assert
        managed.InstanceId.Should().Be("d2-1a2b3c4d");
        managed.TopologyCode.Should().Be("D2");
        managed.Role.Should().Be("cluster-primary");
        managed.NodeIndex.Should().Be(3);
        managed.IsManaged.Should().Be(true);
    }

    /// <summary>A container this tool did not create is not managed.</summary>
    [Fact]
    public void ReadManagedLabels_WhenUnlabelled_IsNotManaged()
    {
        //Arrange
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.example.other"] = "value",
        };

        //Act
        var managed = ContainerMapper.ReadManagedLabels(labels);

        //Assert
        managed.IsManaged.Should().Be(false);
        managed.InstanceId.Should().BeNull();
        managed.NodeIndex.Should().BeNull();
    }

    /// <summary>A missing label set is tolerated.</summary>
    [Fact]
    public void ReadManagedLabels_WhenLabelsAreNull_IsNotManaged()
    {
        //Act
        var managed = ContainerMapper.ReadManagedLabels(null);

        //Assert
        managed.IsManaged.Should().Be(false);
    }

    /// <summary>A node index that is not a number is dropped rather than throwing.</summary>
    [Fact]
    public void ReadManagedLabels_WhenTheNodeIndexIsNotANumber_LeavesItUnset()
    {
        //Arrange
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InstanceLabels.Instance] = "a1-11112222",
            [InstanceLabels.Node] = "first",
        };

        //Act
        var managed = ContainerMapper.ReadManagedLabels(labels);

        //Assert
        managed.NodeIndex.Should().BeNull();
        managed.IsManaged.Should().Be(true);
    }

    /// <summary>
    /// A dual-stack publish comes back from the daemon twice - once for 0.0.0.0 and once for
    /// :: - and both render the same text. Only one survives, and it is the IPv4 one.
    /// </summary>
    [Fact]
    public void DeduplicatePorts_CollapsesADualStackPublish()
    {
        //Arrange
        var ports = new List<PortMapping>
        {
            new()
            {
                ContainerPort = 6379, HostPort = 6400, Protocol = "tcp", HostIp = "::",
                Display = "6379/tcp -> 6400",
            },
            new()
            {
                ContainerPort = 6379, HostPort = 6400, Protocol = "tcp", HostIp = "0.0.0.0",
                Display = "6379/tcp -> 6400",
            },
            new()
            {
                ContainerPort = 16379, HostPort = 16400, Protocol = "tcp", HostIp = "0.0.0.0",
                Display = "16379/tcp -> 16400",
            },
        };

        //Act
        var kept = ContainerMapper.DeduplicatePorts(ports);

        //Assert
        kept.Count.Should().Be(2);
        kept[0].HostIp.Should().Be("0.0.0.0");
        kept[0].Display.Should().Be("6379/tcp -> 6400");
        kept[1].Display.Should().Be("16379/tcp -> 16400");
    }

    /// <summary>Ports that differ only by protocol are not duplicates.</summary>
    [Fact]
    public void DeduplicatePorts_KeepsTheSamePortOnTwoProtocols()
    {
        //Arrange
        var ports = new List<PortMapping>
        {
            new() { ContainerPort = 53, HostPort = 5300, Protocol = "tcp" },
            new() { ContainerPort = 53, HostPort = 5300, Protocol = "udp" },
        };

        //Act
        var kept = ContainerMapper.DeduplicatePorts(ports);

        //Assert
        kept.Count.Should().Be(2);
    }

    /// <summary>A published port reads as an arrow; an unpublished one does not.</summary>
    [Fact]
    public void FormatPortDisplay_RendersPublishedAndUnpublishedPorts()
    {
        //Assert
        ContainerMapper.FormatPortDisplay(6379, 6401, "tcp").Should().Be("6379/tcp -> 6401");
        ContainerMapper.FormatPortDisplay(6379, null, "tcp").Should().Be("6379/tcp");
    }

    /// <summary>An id shorter than twelve characters is left alone.</summary>
    [Fact]
    public void Shorten_TakesTwelveCharactersAtMost()
    {
        //Assert
        ContainerMapper.Shorten("0123456789abcdef").Should().Be("0123456789ab");
        ContainerMapper.Shorten("short").Should().Be("short");
        ContainerMapper.Shorten(null).Should().BeNull();
    }
}
