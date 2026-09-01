using System;
using System.Collections.Generic;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers the catalog of thirteen approved topologies.</summary>
public class TopologyCatalogTests
{
    /// <summary>Every enumeration member appears exactly once.</summary>
    [Fact]
    public void All_ContainsEveryTopologyExactlyOnce()
    {
        //Arrange
        var seen = new HashSet<TopologyId>();

        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            seen.Add(descriptor.Id).Should().Be(true);
        }

        //Assert
        TopologyCatalog.All.Count.Should().Be(13);
        seen.Count.Should().Be(Enum.GetValues<TopologyId>().Length);
    }

    /// <summary>The codes are exactly the approved thirteen, in catalog order.</summary>
    [Fact]
    public void All_HasTheApprovedCodes()
    {
        //Arrange
        var codes = new List<string>();

        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            codes.Add(descriptor.Code);
        }

        //Assert
        string.Join(" ", codes).Should().Be("A1 A2 A3 A5 A6 B1 C1 D2 E3 E4 F3 G1 H1");
    }

    /// <summary>Each topology declares the right number of containers.</summary>
    [Fact]
    public void ContainerCounts_MatchTheDesign()
    {
        //Arrange
        var counts = new List<int>();

        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            counts.Add(descriptor.ContainerCount);
        }

        //Assert
        string.Join(",", counts).Should().Be("1,1,1,1,1,2,6,6,1,1,1,1,5");
    }

    /// <summary>Only the cluster needs bus ports.</summary>
    [Fact]
    public void NeedsBusPorts_IsTrueOnlyForTheCluster()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            //Assert
            descriptor.NeedsBusPorts.Should().Be(descriptor.Id == TopologyId.D2);
        }
    }

    /// <summary>Only the sentinel topology publishes sentinel ports.</summary>
    [Fact]
    public void SentinelPortCount_IsThreeOnlyForTheSentinelTopology()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            //Assert
            descriptor.SentinelPortCount.Should().Be(descriptor.Id == TopologyId.C1 ? 3 : 0);
        }
    }

    /// <summary>Every parameter is properly described, because the form is generated from it.</summary>
    [Fact]
    public void Parameters_AreFullyDescribed()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            foreach (var parameter in descriptor.Parameters)
            {
                //Assert
                parameter.Key.Should().NotBeNullOrWhiteSpace();
                parameter.Label.Should().NotBeNullOrWhiteSpace();
                parameter.HelpText.Should().NotBeNullOrWhiteSpace();

                if (parameter.Kind == TopologyParameterKind.Choice)
                {
                    parameter.Choices.Count.Should().BeGreaterThan(1);
                    parameter.Choices.Should().Contain(parameter.DefaultValue);
                }
            }
        }
    }

    /// <summary>Only the three approved images appear, and no descriptor names a rejected vendor.</summary>
    [Fact]
    public void Images_AreOnlyTheThreeApprovedOnes()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            //Assert
            new[] { "redis:8-alpine", "redis:6.2-alpine", "valkey/valkey:8.1-alpine" }
                .Should().Contain(descriptor.Image);
            descriptor.Detail.Should().NotContain("bitnami");
            descriptor.Summary.Should().NotContain("bitnami");
        }
    }

    /// <summary>Every descriptor carries prose for both the picker and the detail pane.</summary>
    [Fact]
    public void Descriptions_ArePresent()
    {
        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            //Assert
            descriptor.DisplayName.Should().NotBeNullOrWhiteSpace();
            descriptor.Summary.Should().NotBeNullOrWhiteSpace();
            descriptor.Detail.Should().NotBeNullOrWhiteSpace();
            descriptor.Highlights.Count.Should().BeGreaterThan(0);
        }
    }

    /// <summary>Codes parse in any casing, and unknown codes are refused.</summary>
    [Fact]
    public void TryParseCode_AcceptsAnyCasingAndRefusesTheUnknown()
    {
        //Act
        var lower = TopologyCatalog.TryParseCode("d2", out var parsed);
        var unknown = TopologyCatalog.TryParseCode("Z9", out _);

        //Assert
        lower.Should().Be(true);
        parsed.Should().Be(TopologyId.D2);
        unknown.Should().Be(false);
    }
}
