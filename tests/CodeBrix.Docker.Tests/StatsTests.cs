using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class StatsTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    /// <summary>A shell loop that saturates whatever CPU quota the container is given.</summary>
    internal const string BusyLoop = "while :; do :; done";

    [Fact]
    public async Task GetStatsAsync_ForAThrottledBusyLoop_ReportsLiveCpuMemoryAndPidCounters()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("stats", "busybox:latest", "sh", "-c", BusyLoop);
        spec.Limits = new ResourceLimits { Cpus = 0.1, MemoryBytes = ResourceLimits.Megabytes(128) };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var containerId = id;

            var stats = await Poll.UntilAsync(
                token => Client.Containers.GetStatsAsync(containerId, token),
                sample => sample.HasLiveData
                          && (sample.CpuStats?.ThrottlingData?.Periods ?? 0) > 0
                          && (sample.CpuStats?.ThrottlingData?.ThrottledPeriods ?? 0) > 0,
                TimeSpan.FromSeconds(90), "the CPU quota to start throttling the busy loop",
                TimeSpan.FromSeconds(2), cancellation.Token);

            Assert.True(stats.HasLiveData);

            var throttling = stats.CpuStats.ThrottlingData;
            Assert.True(throttling.Periods > 0);
            Assert.True(throttling.ThrottledPeriods > 0);
            Assert.True(throttling.ThrottledTime > 0);
            var ratio = throttling.ThrottleRatio();
            Assert.NotNull(ratio);
            Assert.InRange(ratio.Value, 0d, 1d);

            var cpuPercent = stats.CpuPercent();
            Assert.NotNull(cpuPercent);
            Assert.True(cpuPercent > 0, "A spinning container should report CPU usage.");

            Assert.NotNull(stats.MemoryStats?.Stats);
            Assert.True(stats.MemoryStats.Stats.ContainsKey("anon"),
                "cgroup v2 memory statistics should include the anonymous memory counter.");
            Assert.True(stats.MemoryStats.Stats.ContainsKey("file"),
                "cgroup v2 memory statistics should include the page cache counter.");
            Assert.NotNull(stats.MemoryStats.AnonBytes);
            Assert.NotNull(stats.MemoryStats.Usage);
            Assert.NotNull(stats.MemoryPercent());

            Assert.NotNull(stats.PidsStats);
            Assert.True(stats.PidsStats.Current >= 1);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetStatsAsync_ReportsAPidLimitOnlyWhenOneIsConfigured()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var unlimited = fixture.Spec("pidsfree", "busybox:latest", "sleep", "300");
        var limited = fixture.Spec("pidscap", "busybox:latest", "sleep", "300");
        limited.Limits = new ResourceLimits { PidsLimit = 64 };
        string unlimitedId = null;
        string limitedId = null;

        try
        {
            unlimitedId = await Client.Containers.RunAsync(unlimited, cancellation.Token);
            limitedId = await Client.Containers.RunAsync(limited, cancellation.Token);

            var unlimitedStats = await Client.Containers.GetStatsAsync(unlimitedId, cancellation.Token);
            var limitedStats = await Client.Containers.GetStatsAsync(limitedId, cancellation.Token);

            // cgroup v2 reports "max" for an unlimited container, which the daemon sends as an
            // unsigned 64-bit sentinel; the library surfaces that as null rather than overflowing.
            Assert.NotNull(unlimitedStats.PidsStats);
            Assert.Null(unlimitedStats.PidsStats.Limit);
            Assert.True(unlimitedStats.PidsStats.Current >= 1);

            Assert.Equal(64, limitedStats.PidsStats?.Limit);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(unlimitedId);
            await fixture.RemoveContainerQuietlyAsync(limitedId);
        }
    }

    [Fact]
    public async Task StreamStatsAsync_YieldsRepeatedSamplesForARunningContainer()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("statstream", "busybox:latest", "sh", "-c", BusyLoop);
        spec.Limits = new ResourceLimits { Cpus = 0.25 };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var samples = new List<ContainerStats>();
            using var streamCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);

            try
            {
                await foreach (var sample in Client.Containers.StreamStatsAsync(id, streamCancellation.Token))
                {
                    samples.Add(sample);
                    if (samples.Count >= 3)
                    {
                        await streamCancellation.CancelAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                // Expected: the stream is open-ended and is closed once enough samples have arrived.
            }

            Assert.True(samples.Count >= 2, $"Expected at least two samples, got {samples.Count}.");
            Assert.All(samples, sample => Assert.True(sample.HasLiveData));
            Assert.True(samples[^1].Read >= samples[0].Read);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetStatsAsync_ForAnExitedContainer_ReportsNoLiveData()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("statsdead", "busybox:latest", "sh", "-c", "echo done");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var stats = await Client.Containers.GetStatsAsync(id, cancellation.Token);

            Assert.False(stats.HasLiveData);
            Assert.Null(stats.CpuPercent());
            Assert.Null(stats.MemoryPercent());
            Assert.Null(stats.MemoryStats?.Stats);
            Assert.Null(stats.MemoryStats?.Usage);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }
}
