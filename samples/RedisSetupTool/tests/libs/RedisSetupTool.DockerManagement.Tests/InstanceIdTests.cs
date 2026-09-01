using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers instance ids and the resource names derived from them.</summary>
public class InstanceIdTests
{
    /// <summary>An id is the lowercase code, a dash and eight hexadecimal characters.</summary>
    [Fact]
    public void Create_ProducesTheDocumentedFormat()
    {
        //Act
        var id = InstanceId.Create(TopologyId.D2);

        //Assert
        id.Length.Should().Be(11);
        id[..3].Should().Be("d2-");
        foreach (var character in id[3..])
        {
            char.IsAsciiHexDigitLower(character).Should().Be(true);
        }
    }

    /// <summary>Two ids in a row differ.</summary>
    [Fact]
    public void Create_ProducesADifferentIdEachTime()
    {
        //Act
        var first = InstanceId.Create(TopologyId.A1);
        var second = InstanceId.Create(TopologyId.A1);

        //Assert
        first.Should().NotBe(second);
    }

    /// <summary>The topology can be read back out of an id.</summary>
    [Fact]
    public void TryParseTopology_ReadsTheCodeBack()
    {
        //Act
        var parsed = InstanceId.TryParseTopology("c1-1a2b3c4d", out var topology);

        //Assert
        parsed.Should().Be(true);
        topology.Should().Be(TopologyId.C1);
    }

    /// <summary>An id that names no known topology is rejected.</summary>
    [Fact]
    public void TryParseTopology_WhenTheCodeIsUnknown_ReturnsFalse()
    {
        //Act
        var parsed = InstanceId.TryParseTopology("zz-1a2b3c4d", out _);

        //Assert
        parsed.Should().Be(false);
    }

    /// <summary>Every resource name comes from the id and nothing else.</summary>
    [Fact]
    public void ResourceNames_AreDerivedFromTheIdAlone()
    {
        //Act
        var network = InstanceId.NetworkName("c1-1a2b3c4d");
        var volume = InstanceId.VolumeName("c1-1a2b3c4d", 4);
        var container = InstanceId.ContainerName("c1-1a2b3c4d", "sentinel2");

        //Assert
        network.Should().Be("redissetup-c1-1a2b3c4d");
        volume.Should().Be("redissetup-c1-1a2b3c4d-n4");
        container.Should().Be("redissetup-c1-1a2b3c4d-sentinel2");
    }

    /// <summary>Every generated name is one the daemon will accept.</summary>
    [Fact]
    public void ResourceNames_AreAlwaysValidDaemonNames()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            var id = InstanceId.Create(descriptor.Id);
            InstanceId.IsValidResourceName(InstanceId.NetworkName(id)).Should().Be(true);

            for (var index = 1; index <= descriptor.ContainerCount; index++)
            {
                InstanceId.IsValidResourceName(InstanceId.VolumeName(id, index)).Should().Be(true);
                InstanceId.IsValidResourceName(InstanceId.ContainerName(id, "sentinel" + index))
                    .Should().Be(true);
            }
        }
    }

    /// <summary>Names that break the daemon's rule are rejected.</summary>
    [Fact]
    public void IsValidResourceName_RejectsBadNames()
    {
        //Assert
        InstanceId.IsValidResourceName("-leading-dash").Should().Be(false);
        InstanceId.IsValidResourceName("has space").Should().Be(false);
        InstanceId.IsValidResourceName(new string('a', 64)).Should().Be(false);
        InstanceId.IsValidResourceName(new string('a', 63)).Should().Be(true);
    }
}
