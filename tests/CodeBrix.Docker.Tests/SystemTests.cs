using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class SystemTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task PingAsync_ReportsTheDaemonIsReachable()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Assert.True(await Client.System.PingAsync(cancellation.Token));
    }

    [Fact]
    public async Task GetVersionAsync_ReportsAnApiVersion()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var version = await Client.System.GetVersionAsync(cancellation.Token);

        Assert.False(string.IsNullOrWhiteSpace(version.ApiVersion));
        Assert.False(string.IsNullOrWhiteSpace(version.Version));
        Assert.Equal("linux", version.Os, ignoreCase: true);
    }

    [Fact]
    public async Task GetInfoAsync_ReportsALinuxDaemonOnCgroupV2()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var info = await Client.System.GetInfoAsync(cancellation.Token);

        Assert.Equal("linux", info.OsType);
        Assert.Equal("2", info.CgroupVersion);
        Assert.True(info.NCpu > 0, "The daemon should report at least one CPU.");
        Assert.True(info.MemTotal > 0, "The daemon should report its total memory.");
        Assert.False(string.IsNullOrWhiteSpace(info.ServerVersion));
    }

    [Fact]
    public async Task GetDiskUsageAsync_ReportsImageAndVolumeTotals()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var usage = await Client.System.GetDiskUsageAsync(cancellation.Token);

        Assert.True(usage.ImageCount > 0, "The base images pulled by the fixture should be counted.");
        Assert.True(usage.ImagesSizeBytes > 0);
        Assert.True(usage.ContainerCount >= 0);
        Assert.True(usage.VolumeCount >= 0);
        Assert.True(usage.TotalSizeBytes >= usage.ImagesSizeBytes);
    }

    [Fact]
    public async Task EnsureLinuxDaemonAsync_AcceptsTheConfiguredDaemon()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Client.System.EnsureLinuxDaemonAsync(cancellation.Token);
    }

    [Fact]
    public async Task StreamEventsAsync_YieldsCreateAndStartEventsForANewContainer()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var spec = fixture.Spec("events", "busybox:latest", "sh", "-c", "echo streaming; sleep 1");
        var name = spec.Name;
        var actions = new List<string>();
        string containerId = null;

        var collector = Task.Run(async () =>
        {
            try
            {
                await foreach (var dockerEvent in Client.System.StreamEventsAsync("container", null,
                                   cancellation.Token))
                {
                    if (dockerEvent.Actor?.Attributes is { } attributes
                        && attributes.TryGetValue("name", out var eventName)
                        && string.Equals(eventName, name, StringComparison.Ordinal))
                    {
                        lock (actions)
                        {
                            actions.Add(dockerEvent.Action ?? string.Empty);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The stream is closed by cancelling the token once the expected events have arrived.
            }
        }, CancellationToken.None);

        try
        {
            // Give the event stream a moment to attach before the container is created.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            containerId = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Poll.UntilTrueAsync(_ =>
                {
                    lock (actions)
                    {
                        return Task.FromResult(actions.Contains("create") && actions.Contains("start"));
                    }
                },
                TimeSpan.FromSeconds(30), $"create and start events for '{name}'",
                TimeSpan.FromMilliseconds(200), cancellation.Token);
        }
        finally
        {
            await cancellation.CancelAsync();
            await collector;
            await fixture.RemoveContainerQuietlyAsync(containerId);
        }

        Assert.Contains("create", actions);
        Assert.Contains("start", actions);
    }
}
