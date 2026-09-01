using System.Collections.Generic;
using RedisSetupTool.DockerManagement.Instances;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers the label schema. The exact strings are the database's column names.</summary>
public class InstanceLabelsTests
{
    /// <summary>Every label name is exactly what discovery looks for.</summary>
    [Fact]
    public void Constants_HaveTheDocumentedNames()
    {
        //Assert
        InstanceLabels.Prefix.Should().Be("codebrix.redissetup.");
        InstanceLabels.Instance.Should().Be("codebrix.redissetup.instance");
        InstanceLabels.Topology.Should().Be("codebrix.redissetup.topology");
        InstanceLabels.Role.Should().Be("codebrix.redissetup.role");
        InstanceLabels.Node.Should().Be("codebrix.redissetup.node");
        InstanceLabels.Name.Should().Be("codebrix.redissetup.name");
        InstanceLabels.Created.Should().Be("codebrix.redissetup.created");
        InstanceLabels.Port.Should().Be("codebrix.redissetup.port");
        InstanceLabels.BusPort.Should().Be("codebrix.redissetup.busport");
        InstanceLabels.AnnounceIp.Should().Be("codebrix.redissetup.announceip");
        InstanceLabels.Image.Should().Be("codebrix.redissetup.image");
        InstanceLabels.Secret.Should().Be("codebrix.redissetup.secret");
        InstanceLabels.Users.Should().Be("codebrix.redissetup.users");
        InstanceLabels.Service.Should().Be("codebrix.redissetup.service");
        InstanceLabels.Resource.Should().Be("codebrix.redissetup.resource");
    }

    /// <summary>A null value is what makes the daemon match on presence rather than equality.</summary>
    [Fact]
    public void PresenceFilter_CarriesANullValue()
    {
        //Act
        var filter = InstanceLabels.PresenceFilter;

        //Assert
        filter.Count.Should().Be(1);
        filter[InstanceLabels.Instance].Should().BeNull();
    }

    /// <summary>An instance filter matches one id.</summary>
    [Fact]
    public void InstanceFilter_RoundTripsTheId()
    {
        //Act
        var filter = InstanceLabels.InstanceFilter("a1-7f3c2b1d");

        //Assert
        filter[InstanceLabels.Instance].Should().Be("a1-7f3c2b1d");
    }

    /// <summary>Reading an absent label gives null rather than throwing.</summary>
    [Fact]
    public void Read_WhenTheLabelIsAbsent_ReturnsNull()
    {
        //Arrange
        var labels = new Dictionary<string, string> { [InstanceLabels.Topology] = "A1" };

        //Assert
        InstanceLabels.Read(labels, InstanceLabels.Topology).Should().Be("A1");
        InstanceLabels.Read(labels, InstanceLabels.Secret).Should().BeNull();
        InstanceLabels.Read(null, InstanceLabels.Secret).Should().BeNull();
    }
}
