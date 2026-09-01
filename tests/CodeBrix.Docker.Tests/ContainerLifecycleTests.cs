using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class ContainerLifecycleTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task RunListInspectStopRemove_WalksAContainerThroughItsWholeLifecycle()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("lifecycle", "alpine:latest", "sleep", "300");
        spec.Labels["codebrix.docker.role"] = "lifecycle";
        var name = spec.Name;
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            Assert.False(string.IsNullOrWhiteSpace(id));

            var listed = await Client.Containers.ListAsync(all: false, DockerTestFixture.TestLabelFilter,
                cancellation.Token);
            var summary = Assert.Single(listed, container => container.Id == id);
            Assert.Equal(name, summary.DisplayName);
            Assert.True(summary.IsRunning);
            Assert.Equal("alpine:latest", summary.Image);

            var inspect = await Client.Containers.InspectAsync(name, cancellation.Token);
            Assert.Equal(id, inspect.Id);
            Assert.Equal(name, inspect.DisplayName);
            Assert.DoesNotContain('/', inspect.DisplayName);
            Assert.True(inspect.IsRunning);
            Assert.Equal("running", inspect.State?.Status);
            Assert.NotNull(inspect.Config?.Labels);
            Assert.Equal(DockerTestFixture.LabelValue,
                inspect.Config.Labels[DockerTestFixture.LabelName]);
            Assert.Equal("lifecycle", inspect.Config.Labels["codebrix.docker.role"]);

            await Client.Containers.StopAsync(id, timeoutSeconds: 5, cancellation.Token);

            var stopped = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.False(stopped.IsRunning);
            Assert.Equal("exited", stopped.State?.Status);
            Assert.NotNull(stopped.State?.FinishedAt);

            await Client.Containers.RemoveAsync(id, force: false, removeVolumes: false, cancellation.Token);
            var removedId = id;
            id = null;

            await Assert.ThrowsAsync<DockerContainerNotFoundException>(
                () => Client.Containers.InspectAsync(removedId, cancellation.Token));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task RunAsync_WithAutoRemove_DeletesTheContainerOnceItExits()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("autoremove", "alpine:latest", "sh", "-c", "sleep 2; exit 0");
        spec.AutoRemove = true;
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var exitCode = await Client.Containers.WaitForExitAsync(id, cancellation.Token);
            Assert.Equal(0, exitCode);

            var removedId = id;
            await Poll.UntilTrueAsync(async token =>
                {
                    try
                    {
                        await Client.Containers.InspectAsync(removedId, token);
                        return false;
                    }
                    catch (DockerContainerNotFoundException)
                    {
                        return true;
                    }
                },
                TimeSpan.FromSeconds(60), "the auto-removed container to disappear",
                cancellationToken: cancellation.Token);

            id = null;
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task WaitForExitAsync_ReturnsTheExitCodeOfTheContainerProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("exitcode", "alpine:latest", "sh", "-c", "exit 7");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var exitCode = await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            Assert.Equal(7, exitCode);

            var inspect = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.Equal(7, inspect.State?.ExitCode);
            Assert.False(inspect.State?.OomKilled);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetLogsAsync_DemultiplexesStandardOutputFromStandardError()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("logs", "alpine:latest", "sh", "-c", "echo out; echo err 1>&2");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var logs = await Client.Containers.GetLogsAsync(id, cancellationToken: cancellation.Token);

            Assert.Contains("out", logs.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("err", logs.Stdout, StringComparison.Ordinal);
            Assert.Contains("err", logs.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("out", logs.Stderr, StringComparison.Ordinal);
            Assert.False(logs.IsEmpty);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetLogsAsync_WithTail_ReturnsOnlyTheMostRecentLines()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("logtail", "alpine:latest", "sh", "-c",
            "for i in 1 2 3 4 5; do echo line$i; done");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var tail = await Client.Containers.GetLogsAsync(id, tail: 2, cancellationToken: cancellation.Token);

            Assert.Contains("line4", tail.Stdout, StringComparison.Ordinal);
            Assert.Contains("line5", tail.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("line1", tail.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("line3", tail.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetLogsAsync_WithTimestamps_PrefixesEveryLineWithAnRfc3339Instant()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("logstamp", "alpine:latest", "sh", "-c", "echo stamped");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var logs = await Client.Containers.GetLogsAsync(id, timestamps: true,
                cancellationToken: cancellation.Token);

            var firstLine = logs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
            var prefix = firstLine.Split(' ', 2)[0];

            Assert.True(DateTimeOffset.TryParse(prefix, out var timestamp),
                $"'{prefix}' should be a parseable timestamp.");
            Assert.True(timestamp > DateTimeOffset.UtcNow.AddHours(-1));
            Assert.Contains("stamped", firstLine, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecAsync_ReturnsStandardOutputAndASuccessExitCode()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("exec", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var result = await Client.Containers.ExecAsync(id, ["sh", "-c", "echo hello-from-exec"],
                cancellationToken: cancellation.Token);

            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Succeeded);
            Assert.Contains("hello-from-exec", result.Stdout, StringComparison.Ordinal);
            Assert.Empty(result.Stderr.Trim());
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecAsync_ForAFailingCommand_ReturnsItsExitCodeAndStandardError()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("execfail", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var result = await Client.Containers.ExecAsync(id, ["sh", "-c", "echo boom 1>&2; exit 3"],
                cancellationToken: cancellation.Token);

            Assert.Equal(3, result.ExitCode);
            Assert.False(result.Succeeded);
            Assert.Contains("boom", result.Stderr, StringComparison.Ordinal);
            Assert.Empty(result.Stdout.Trim());
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecAsync_HonoursTheRequestedUserAndWorkingDirectory()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("execuser", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var result = await Client.Containers.ExecAsync(id, ["sh", "-c", "id -un; pwd; echo $CB_MARKER"],
                user: "nobody", workingDir: "/tmp", env: ["CB_MARKER=marked"],
                cancellationToken: cancellation.Token);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("nobody", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("/tmp", result.Stdout, StringComparison.Ordinal);
            Assert.Contains("marked", result.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task KillAsync_TerminatesTheContainerWithExitCode137()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("kill", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Client.Containers.KillAsync(id, cancellationToken: cancellation.Token);
            var exitCode = await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            Assert.Equal(137, exitCode);

            var inspect = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.False(inspect.IsRunning);
            Assert.Equal(137, inspect.State?.ExitCode);
            Assert.False(inspect.State?.OomKilled);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task RestartAsync_StartsAFreshContainerProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("restart", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var before = await Client.Containers.InspectAsync(id, cancellation.Token);
            var firstStart = before.State?.StartedAt;
            Assert.NotNull(firstStart);

            await Client.Containers.RestartAsync(id, timeoutSeconds: 5, cancellation.Token);

            var after = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.True(after.IsRunning);
            Assert.NotNull(after.State?.StartedAt);
            Assert.True(after.State.StartedAt > firstStart,
                $"Restart should record a later start time ({after.State.StartedAt} vs {firstStart}).");
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task StartAsync_OnAnAlreadyRunningContainer_Succeeds()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("startagain", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.CreateAsync(spec, cancellation.Token);
            await Client.Containers.StartAsync(id, cancellation.Token);

            // The daemon answers a redundant start with 304 Not Modified, which is not a failure.
            await Client.Containers.StartAsync(id, cancellation.Token);

            var inspect = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.True(inspect.IsRunning);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }
}
