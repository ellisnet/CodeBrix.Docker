using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Exec;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Interactive exec sessions against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class ExecSessionTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public ExecSessionTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>A typed command echoes and its output comes back.</summary>
    [Fact]
    public async Task OpenShellAsync_RunsATypedCommand()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("shell");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "redis:8-alpine", "--port", "6379", "--save", "", "--appendonly", "no");

        try
        {
            await using var session = await _fixture.Docker.OpenShellAsync(name, null,
                TestContext.Current.CancellationToken);

            //Act
            await session.SendAsync("echo MARKER-$((6*7))\n", TestContext.Current.CancellationToken);
            var transcript = await ReadUntilAsync(session, "MARKER-42", TimeSpan.FromSeconds(15));

            //Assert
            session.ShellPath.Should().Be("/bin/sh");
            session.IsTty.Should().Be(true);
            session.UsesRawFraming.Should().Be(true);
            transcript.Should().Contain("MARKER-42");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>A resize is honoured inside the container, rows first.</summary>
    [Fact]
    public async Task ResizeAsync_ChangesTheTerminalInsideTheContainer()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("resize");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            await using var session = await _fixture.Docker.OpenShellAsync(name, null,
                TestContext.Current.CancellationToken);

            //Act
            await session.SendAsync("stty size\n", TestContext.Current.CancellationToken);
            var before = await ReadUntilAsync(session, "24 80", TimeSpan.FromSeconds(15));

            await session.ResizeAsync(40, 120, TestContext.Current.CancellationToken);
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await session.SendAsync("stty size\n", TestContext.Current.CancellationToken);
            var after = await ReadUntilAsync(session, "40 120", TimeSpan.FromSeconds(15));

            //Assert
            before.Should().Contain("24 80");
            after.Should().Contain("40 120");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>The shell's exit code comes back through the session.</summary>
    [Fact]
    public async Task WaitForExitAsync_ReportsTheShellsExitCode()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("exit");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            await using var session = await _fixture.Docker.OpenShellAsync(name, null,
                TestContext.Current.CancellationToken);

            //Act
            await session.SendAsync("exit 7\n", TestContext.Current.CancellationToken);
            await ReadToEndAsync(session, TimeSpan.FromSeconds(15));
            var exitCode = await session.WaitForExitAsync(TestContext.Current.CancellationToken);

            //Assert
            exitCode.Should().Be(7);
            session.IsRunning.Should().Be(false);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Disposing while a read is outstanding does not throw.</summary>
    [Fact]
    public async Task DisposeAsync_WhileReading_DoesNotThrow()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("dispose");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "="
                + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            var session = await _fixture.Docker.OpenShellAsync(name, null,
                TestContext.Current.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var reading = Task.Run(async () =>
            {
                try
                {
                    var buffer = new byte[1024];
                    while (true)
                    {
                        var read = await session.ReadAsync(buffer, cancellation.Token);
                        if (read.EndOfStream)
                        {
                            return;
                        }
                    }
                }
                catch (Exception)
                {
                    //Disposal races the read; either outcome is acceptable.
                }
            }, TestContext.Current.CancellationToken);

            //Act
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await session.DisposeAsync();
            await cancellation.CancelAsync();
            await reading;

            //Assert
            session.IsRunning.Should().Be(false);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    private static async Task<string> ReadUntilAsync(IExecSession session, string marker,
        TimeSpan timeout)
    {
        var transcript = new StringBuilder();
        var buffer = new byte[4096];
        using var cancellation = new CancellationTokenSource(timeout);

        while (!cancellation.IsCancellationRequested)
        {
            var read = await session.ReadAsync(buffer, cancellation.Token);
            if (read.EndOfStream)
            {
                break;
            }

            transcript.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
            if (transcript.ToString().Contains(marker, StringComparison.Ordinal))
            {
                break;
            }
        }

        return transcript.ToString();
    }

    private static async Task<string> ReadToEndAsync(IExecSession session, TimeSpan timeout)
    {
        var transcript = new StringBuilder();
        var buffer = new byte[4096];
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            while (true)
            {
                var read = await session.ReadAsync(buffer, cancellation.Token);
                if (read.EndOfStream)
                {
                    break;
                }

                transcript.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
            }
        }
        catch (OperationCanceledException)
        {
            //The deadline closed the read.
        }

        return transcript.ToString();
    }
}
