using System;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers request validation, which never touches the daemon.</summary>
public class TopologyRequestValidationTests
{
    /// <summary>The defaults on every topology are valid as they stand.</summary>
    [Fact]
    public void Validate_WithDefaults_FindsNoProblems()
    {
        //Arrange
        var service = Service();

        //Act
        foreach (var descriptor in TopologyCatalog.All)
        {
            //Assert
            service.Validate(new TopologyRequest { TopologyId = descriptor.Id })
                .Count.Should().Be(0);
        }
    }

    /// <summary>A required parameter blanked out is refused.</summary>
    [Fact]
    public void Validate_WhenARequiredParameterIsBlank_ReportsIt()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.A2 };
        request.Parameters["password"] = string.Empty;

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems.Count.Should().Be(1);
        problems[0].Should().Contain("Password is required");
    }

    /// <summary>An integer below the minimum is refused.</summary>
    [Fact]
    public void Validate_WhenAnIntegerIsOutOfRange_ReportsIt()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.G1 };
        request.Parameters["containerMemoryMb"] = "4";

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems[0].Should().Contain("at least 16");
    }

    /// <summary>Text where a number belongs is refused.</summary>
    [Fact]
    public void Validate_WhenAnIntegerIsNotANumber_ReportsIt()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.D2 };
        request.Parameters["nodeTimeoutMs"] = "soon";

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems[0].Should().Contain("whole number");
    }

    /// <summary>A choice outside the list is refused, and the list is named.</summary>
    [Fact]
    public void Validate_WhenAChoiceIsUnknown_ReportsTheAllowedValues()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.A6 };
        request.Parameters["policy"] = "allkeys-guess";

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems[0].Should().Contain("allkeys-lru");
    }

    /// <summary>A user line that is not name:password:permissions is refused.</summary>
    [Fact]
    public void Validate_WhenAUserLineDoesNotParse_ReportsTheLine()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.A3 };
        request.Parameters["users"] = "app-with-no-colons";

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems[0].Should().Contain("app-with-no-colons");
        problems[0].Should().Contain("name:password:permissions");
    }

    /// <summary>A blank instance name falls back to a generated one rather than failing.</summary>
    [Fact]
    public void Validate_WhenTheInstanceNameIsBlank_IsStillValid()
    {
        //Arrange
        var request = new TopologyRequest { TopologyId = TopologyId.A1, InstanceName = "   " };

        //Act
        var problems = Service().Validate(request);

        //Assert
        problems.Count.Should().Be(0);
    }

    private static RedisTopologyService Service() =>
        new(new DockerManager(), new HostPortAllocator(
            _ => Task.FromResult<System.Collections.Generic.IReadOnlyCollection<int>>([]),
            _ => true, new PortAllocationOptions()));
}
