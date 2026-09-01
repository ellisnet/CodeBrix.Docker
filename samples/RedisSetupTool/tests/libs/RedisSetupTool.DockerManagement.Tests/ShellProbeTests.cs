using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Exec;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Shell probing against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class ShellProbeTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public ShellProbeTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The Redis image ships no bash, so the probe falls through to the shell it does have.</summary>
    [Fact]
    public async Task ProbeShellAsync_OnTheRedisImage_ResolvesShNotBash()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("probe");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "redis:8-alpine", "--port", "6379", "--save", "", "--appendonly", "no");

        try
        {
            //Act
            var result = await _fixture.Docker.ProbeShellAsync(name, null,
                TestContext.Current.CancellationToken);

            //Assert
            result.Found.Should().Be(true);
            result.ShellPath.Should().Be("/bin/sh");
            result.Tried.Should().Contain("/bin/bash");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>
    /// A shell that is not there does not throw and does not hang: the daemon writes the runtime's
    /// message on the ordinary output stream and reports exit code 127.
    /// </summary>
    [Fact]
    public async Task ProbeShellAsync_WhenTheOnlyCandidateIsMissing_ReportsTheRuntimeMessage()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("nobash");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "redis:8-alpine", "--port", "6379", "--save", "", "--appendonly", "no");

        try
        {
            //Act
            var result = await _fixture.Docker.ProbeShellAsync(name, ["/bin/bash"],
                TestContext.Current.CancellationToken);

            //Assert
            result.Found.Should().Be(false);
            result.ShellPath.Should().BeNull();
            result.Message.Should().Contain("no such file or directory");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>A stopped container is told to start rather than probed.</summary>
    [Fact]
    public async Task ProbeShellAsync_WhenTheContainerIsStopped_SaysToStartItFirst()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("stopped");
        DockerCli.Run("create", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var result = await _fixture.Docker.ProbeShellAsync(name, null,
                TestContext.Current.CancellationToken);

            //Assert
            result.Found.Should().Be(false);
            result.Message.Should().Contain("Start the container");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Opening a console into an image with no candidate shell is refused clearly.</summary>
    [Fact]
    public async Task OpenShellAsync_WhenNoShellExists_ThrowsNoShellAvailable()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("noshell");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var act = () => _fixture.Docker.OpenShellAsync(name,
                new ExecSessionOptions { ShellCandidates = ["/bin/bash"] },
                TestContext.Current.CancellationToken);

            //Assert
            var thrown = await act.Should().ThrowAsync<NoShellAvailableException>();
            thrown.And.Message.Should().Contain("/bin/bash");
            thrown.And.Result.Found.Should().Be(false);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }
}
