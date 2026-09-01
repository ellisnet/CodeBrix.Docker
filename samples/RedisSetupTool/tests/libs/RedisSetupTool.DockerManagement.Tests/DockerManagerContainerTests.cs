using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Container operations against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class DockerManagerContainerTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public DockerManagerContainerTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The first live assertion of the whole suite: a published port must actually be reachable from
    /// the test process, because every one of the thirteen topologies depends on it.
    /// </summary>
    [Fact]
    public async Task PortBinding_WhenAContainerPublishesAPort_TheHostCanReachIt()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("port");
        var hostPort = FreePort();
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "-p", hostPort.ToString(CultureInfo.InvariantCulture) + ":6379",
            "redis:8-alpine", "--port", "6379", "--save", "", "--appendonly", "no");

        try
        {
            //Act
            var answered = await WaitForPongAsync(hostPort, TimeSpan.FromSeconds(20));
            var containers = await _fixture.Docker.ListContainersAsync(false,
                TestContext.Current.CancellationToken);

            //Assert
            answered.Should().Be(true);

            var found = false;
            foreach (var container in containers)
            {
                if (container.Name == name)
                {
                    found = true;
                    container.Ports.Count.Should().BeGreaterThan(0);
                    container.Ports[0].HostPort.Should().Be(hostPort);
                    container.Ports[0].Display.Should().Contain("-> "
                        + hostPort.ToString(CultureInfo.InvariantCulture));
                }
            }

            found.Should().Be(true);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Inspect reads the labels, the mounts and the limits back.</summary>
    [Fact]
    public async Task InspectContainerAsync_ReadsTheLabelsAndTheState()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("inspect");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "--label", "codebrix.redissetup.instance=a1-deadbeef",
            "--label", "codebrix.redissetup.topology=A1",
            "--label", "codebrix.redissetup.role=primary",
            "--label", "codebrix.redissetup.node=1",
            "alpine:latest", "sleep", "600");

        try
        {
            //Act
            var detail = await _fixture.Docker.InspectContainerAsync(name,
                TestContext.Current.CancellationToken);

            //Assert
            detail.Name.Should().Be(name);
            detail.IsRunning.Should().Be(true);
            detail.ShortId.Length.Should().Be(12);
            detail.Labels["codebrix.redissetup.topology"].Should().Be("A1");
            detail.Command.Should().Contain("sleep");
            detail.Limits.Should().NotBeNull();
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Stop, start, restart, kill and remove all work through the facade.</summary>
    [Fact]
    public async Task Lifecycle_StopsStartsRestartsKillsAndRemoves()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("lifecycle");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "600");
        var token = TestContext.Current.CancellationToken;

        try
        {
            //Act
            await _fixture.Docker.StopContainerAsync(name, 5, token);
            var stopped = await _fixture.Docker.InspectContainerAsync(name, token);

            await _fixture.Docker.StartContainerAsync(name, token);
            var started = await _fixture.Docker.InspectContainerAsync(name, token);

            await _fixture.Docker.RestartContainerAsync(name, 5, token);
            var restarted = await _fixture.Docker.InspectContainerAsync(name, token);

            await _fixture.Docker.KillContainerAsync(name, "SIGKILL", token);
            var killed = await _fixture.Docker.InspectContainerAsync(name, token);

            await _fixture.Docker.RemoveContainerAsync(name, true, false, token);
            var act = () => _fixture.Docker.InspectContainerAsync(name, token);

            //Assert
            stopped.IsRunning.Should().Be(false);
            started.IsRunning.Should().Be(true);
            restarted.IsRunning.Should().Be(true);
            restarted.RestartCount.Should().Be(0);
            killed.IsRunning.Should().Be(false);

            var thrown = await act.Should().ThrowAsync<DockerManagementException>();
            thrown.And.IsNotFound.Should().Be(true);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Standard output and standard error come back apart.</summary>
    [Fact]
    public async Task GetLogsAsync_KeepsTheTwoStreamsApart()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("logs");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sh", "-c", "echo TO-STDOUT; echo TO-STDERR 1>&2; sleep 300");

        try
        {
            //Act
            await Task.Delay(700, TestContext.Current.CancellationToken);
            var logs = await _fixture.Docker.GetLogsAsync(name, 100, false,
                TestContext.Current.CancellationToken);

            //Assert
            logs.Stdout.Should().Contain("TO-STDOUT");
            logs.Stderr.Should().Contain("TO-STDERR");
            logs.Stdout.Should().NotContain("TO-STDERR");
            logs.IsEmpty.Should().Be(false);
            logs.Combined.Should().Contain("TO-STDOUT");
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Statistics come back with live figures, one sample and as a stream.</summary>
    [Fact]
    public async Task Stats_ReturnOneSampleAndAStream()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("stats");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sh", "-c", "while true; do sleep 1; done");

        try
        {
            //Act
            var single = await _fixture.Docker.GetStatsAsync(name,
                TestContext.Current.CancellationToken);

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            var samples = 0;
            await foreach (var sample in _fixture.Docker.StreamStatsAsync(name, cancellation.Token))
            {
                sample.ContainerId.Should().NotBeNullOrEmpty();
                if (++samples == 3)
                {
                    await cancellation.CancelAsync();
                    break;
                }
            }

            //Assert
            single.HasLiveData.Should().Be(true);
            single.MemoryUsageBytes.Should().NotBeNull();
            samples.Should().Be(3);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>A one-shot command returns its output and its exit code.</summary>
    [Fact]
    public async Task RunCommandAsync_ReturnsOutputAndExitCode()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("exec");
        DockerCli.Run("run", "-d", "--name", name,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var ok = await _fixture.Docker.RunCommandAsync(name, ["sh", "-c", "echo MARKER-$((6*7))"],
                cancellationToken: TestContext.Current.CancellationToken);
            var bad = await _fixture.Docker.RunCommandAsync(name, ["sh", "-c", "exit 3"],
                cancellationToken: TestContext.Current.CancellationToken);

            //Assert
            ok.Stdout.Should().Contain("MARKER-42");
            ok.ExitCode.Should().Be(0);
            ok.Succeeded.Should().Be(true);
            bad.ExitCode.Should().Be(3);
            bad.Succeeded.Should().Be(false);
        }
        finally
        {
            DockerCli.RemoveQuietly(name);
        }
    }

    /// <summary>Label filtering finds only the containers this tool created.</summary>
    [Fact]
    public async Task ListManagedContainersAsync_FindsOnlyLabelledContainers()
    {
        //Arrange
        var managed = RedisSetupToolFixture.NewName("managed");
        var plain = RedisSetupToolFixture.NewName("plain");
        var instanceId = "a1-" + Guid.NewGuid().ToString("N")[..8];

        DockerCli.Run("run", "-d", "--name", managed,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "--label", "codebrix.redissetup.instance=" + instanceId,
            "--label", "codebrix.redissetup.topology=A1",
            "--label", "codebrix.redissetup.role=primary",
            "--label", "codebrix.redissetup.node=1",
            "alpine:latest", "sleep", "300");
        DockerCli.Run("run", "-d", "--name", plain,
            "--label", RedisSetupToolFixture.TestLabelName + "=" + RedisSetupToolFixture.TestLabelValue,
            "alpine:latest", "sleep", "300");

        try
        {
            //Act
            var all = await _fixture.Docker.ListManagedContainersAsync(
                TestContext.Current.CancellationToken);
            var mine = await _fixture.Docker.ListInstanceContainersAsync(instanceId,
                TestContext.Current.CancellationToken);

            //Assert
            var names = new List<string>();
            foreach (var container in all)
            {
                names.Add(container.Name);
            }

            names.Should().Contain(managed);
            names.Should().NotContain(plain);

            mine.Count.Should().Be(1);
            mine[0].InstanceId.Should().Be(instanceId);
            mine[0].TopologyCode.Should().Be("A1");
            mine[0].Role.Should().Be("primary");
            mine[0].NodeIndex.Should().Be(1);
            mine[0].IsManaged.Should().Be(true);
        }
        finally
        {
            DockerCli.RemoveQuietly(managed);
            DockerCli.RemoveQuietly(plain);
        }
    }

    /// <summary>The daemon reports what it is.</summary>
    [Fact]
    public async Task GetDaemonInfoAsync_ReportsAReachableLinuxDaemon()
    {
        //Act
        var info = await _fixture.Docker.GetDaemonInfoAsync(TestContext.Current.CancellationToken);
        var usage = await _fixture.Docker.GetDiskUsageAsync(TestContext.Current.CancellationToken);

        //Assert
        info.IsReachable.Should().Be(true);
        info.OsType.Should().Be("linux");
        info.ServerVersion.Should().NotBeNullOrEmpty();
        info.ApiVersion.Should().NotBeNullOrEmpty();
        usage.TotalSizeBytes.Should().BeGreaterThan(0);
    }

    /// <summary>The event stream produces the events a container start causes.</summary>
    [Fact]
    public async Task StreamEventsAsync_ReportsContainerEvents()
    {
        //Arrange
        var name = RedisSetupToolFixture.NewName("events");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(20));

        var seen = new List<string>();
        var reader = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in _fixture.Docker.StreamEventsAsync(cancellation.Token))
                {
                    seen.Add(line.Type + " " + line.Action + " " + line.ActorName);
                    if (line.ActorName == name && line.Action == "start")
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //The deadline closed the stream.
            }
        }, cancellation.Token);

        try
        {
            //Act
            await Task.Delay(400, cancellation.Token);
            DockerCli.Run("run", "-d", "--name", name,
                "--label", RedisSetupToolFixture.TestLabelName + "="
                    + RedisSetupToolFixture.TestLabelValue,
                "alpine:latest", "sleep", "60");
            await reader;

            //Assert
            seen.Should().Contain("container start " + name);
        }
        finally
        {
            await cancellation.CancelAsync();
            DockerCli.RemoveQuietly(name);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitForPongAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                await using var stream = client.GetStream();
                var ping = Encoding.ASCII.GetBytes("PING\r\n");
                await stream.WriteAsync(ping);

                var buffer = new byte[64];
                var read = await stream.ReadAsync(buffer);
                if (Encoding.ASCII.GetString(buffer, 0, read).Contains("PONG",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (SocketException)
            {
                //The container is still starting.
            }
            catch (System.IO.IOException)
            {
                //The container is still starting.
            }

            await Task.Delay(250);
        }

        return false;
    }
}
