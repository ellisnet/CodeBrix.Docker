using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Seed test proving the project runs.</summary>
public class BasicTests
{
    /// <summary>The suite executes.</summary>
    [Fact]
    public void can_run_tests()
    {
        //Arrange
        var isRunning = true;

        //Assert
        isRunning.Should().Be(true);
    }
}
