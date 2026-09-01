using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class DiagnosticsTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task GetCpuThrottlingAsync_ForATightlyCappedBusyLoop_ReportsSevereThrottling()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("throttle", "busybox:latest", "sh", "-c", StatsTests.BusyLoop);
        spec.Limits = new ResourceLimits { Cpus = 0.1 };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var containerId = id;

            var report = await Poll.UntilAsync(
                token => Client.Diagnostics.GetCpuThrottlingAsync(containerId, token),
                sample => sample.HasLiveData && sample.Periods > 0
                          && sample.Severity >= ThrottleSeverity.High,
                TimeSpan.FromSeconds(90), "the CPU quota to throttle the busy loop severely",
                TimeSpan.FromSeconds(2), cancellation.Token);

            Assert.True(report.HasLiveData);
            Assert.Equal(spec.Name, report.ContainerName);
            Assert.True(report.Periods > 0);
            Assert.True(report.ThrottledPeriods > 0);
            Assert.True(report.ThrottledTimeNanos > 0);
            Assert.True(report.ThrottledTime > TimeSpan.Zero);
            Assert.InRange(report.ThrottleRatio, 0.25d, 1d);
            Assert.True(report.Severity is ThrottleSeverity.High or ThrottleSeverity.Critical,
                $"Expected High or Critical severity, got {report.Severity}.");
            Assert.Contains("throttled", report.Interpretation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%", report.Interpretation, StringComparison.Ordinal);
            Assert.Contains(spec.Name, report.Interpretation, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetCpuThrottlingAsync_ForAStoppedContainer_ReportsCountersAsUnavailable()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("throttledead", "busybox:latest", "sh", "-c", "echo finished");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var report = await Client.Diagnostics.GetCpuThrottlingAsync(id, cancellation.Token);

            Assert.False(report.HasLiveData);
            Assert.Equal(ThrottleSeverity.None, report.Severity);
            Assert.Equal(0, report.Periods);
            Assert.Equal(0d, report.ThrottleRatio);
            Assert.Contains("unavailable", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetMemoryBreakdownAsync_SeparatesPageCacheFromApplicationMemory()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("memory", "alpine:latest", "sh", "-c",
            "dd if=/dev/zero of=/tmp/filler bs=1M count=50; sleep 300");
        spec.Limits = new ResourceLimits
        {
            MemoryBytes = ResourceLimits.Megabytes(256),
            MemorySwapBytes = ResourceLimits.Megabytes(256),
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var containerId = id;

            var report = await Poll.UntilAsync(
                token => Client.Diagnostics.GetMemoryBreakdownAsync(containerId, token),
                sample => sample.HasLiveData && sample.FileBytes >= ResourceLimits.Megabytes(40),
                TimeSpan.FromSeconds(90), "the written file to show up as page cache",
                TimeSpan.FromSeconds(2), cancellation.Token);

            Assert.True(report.HasLiveData);
            Assert.Equal(ResourceLimits.Megabytes(256), report.LimitBytes);
            Assert.NotNull(report.FileBytes);
            Assert.InRange(report.FileBytes.Value, ResourceLimits.Megabytes(40),
                ResourceLimits.Megabytes(60));
            Assert.NotNull(report.AnonBytes);
            Assert.True(report.AnonBytes < report.FileBytes,
                "The 50 MB file should dominate the shell's own memory.");
            Assert.True(report.IsPageCacheDominated,
                $"Usage {report.UsageBytes} with {report.FileBytes} of cache should read as cache-dominated.");
            Assert.NotNull(report.UsagePercent);
            Assert.NotNull(report.EffectiveUsagePercent);
            Assert.True(report.EffectiveUsagePercent < report.UsagePercent);
            Assert.Contains("page cache", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetMemoryBreakdownAsync_WithoutAMemoryLimit_ReportsNoPercentages()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("memfree", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var report = await Client.Diagnostics.GetMemoryBreakdownAsync(id, cancellation.Token);

            Assert.True(report.HasLiveData);
            Assert.Null(report.LimitBytes);
            Assert.Null(report.UsagePercent);
            Assert.Null(report.EffectiveUsagePercent);
            Assert.True(report.UsageBytes > 0);
            Assert.Contains("no memory limit", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CheckOomAsync_ForAnOomKilledContainer_ExplainsTheKill()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var spec = OomSpecs.MemoryHog(fixture);
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var report = await Client.Diagnostics.CheckOomAsync(id, cancellation.Token);

            Assert.True(report.WasOomKilled);
            Assert.Equal(137, report.ExitCode);
            Assert.False(report.IsRunning);
            Assert.Equal(0, report.RestartCount);
            Assert.NotNull(report.FinishedAt);
            Assert.Equal(ResourceLimits.Megabytes(64), report.MemoryLimitBytes);
            Assert.Contains("OOM killer", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task CheckOomAsync_ForAContainerThatExitedCleanly_ReportsNoKill()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("nooom", "alpine:latest", "sh", "-c", "exit 0");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            await Client.Containers.WaitForExitAsync(id, cancellation.Token);

            var report = await Client.Diagnostics.CheckOomAsync(id, cancellation.Token);

            Assert.False(report.WasOomKilled);
            Assert.Equal(0, report.ExitCode);
            Assert.Contains("exited normally", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task WaitForHealthyAsync_ReturnsOnceTheHealthcheckPasses()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var spec = fixture.Spec("healthy", "nginx:alpine");
        spec.Healthcheck = new HealthcheckSpec
        {
            Test = ["CMD-SHELL", "wget -q -O /dev/null http://localhost/ || exit 1"],
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(3),
            StartPeriod = TimeSpan.FromSeconds(1),
            Retries = 5,
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Client.Diagnostics.WaitForHealthyAsync(id, TimeSpan.FromMinutes(2), cancellation.Token);

            var report = await Client.Diagnostics.GetHealthAsync(id, cancellation.Token);
            Assert.True(report.HasHealthcheck);
            Assert.True(report.IsHealthy);
            Assert.Equal("healthy", report.Status);
            Assert.Equal(0, report.FailingStreak);
            Assert.NotEmpty(report.RecentLogs);
            Assert.Contains("passing its healthcheck", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task WaitForHealthyAsync_WithoutAHealthcheck_ThrowsDockerException()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("nohealth", "busybox:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var exception = await Assert.ThrowsAnyAsync<DockerException>(
                () => Client.Diagnostics.WaitForHealthyAsync(id, TimeSpan.FromSeconds(10),
                    cancellation.Token));

            Assert.Contains("no healthcheck", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task WaitForHealthyAsync_WhenTheHealthcheckNeverPasses_ThrowsTimeoutException()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("unhealthy", "busybox:latest", "sleep", "300");
        spec.Healthcheck = new HealthcheckSpec
        {
            Test = ["CMD", "false"],
            Interval = TimeSpan.FromSeconds(1),
            Timeout = TimeSpan.FromSeconds(1),
            Retries = 1,
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Assert.ThrowsAsync<TimeoutException>(
                () => Client.Diagnostics.WaitForHealthyAsync(id, TimeSpan.FromSeconds(8), cancellation.Token));

            var report = await Client.Diagnostics.GetHealthAsync(id, cancellation.Token);
            Assert.True(report.HasHealthcheck);
            Assert.False(report.IsHealthy);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetHealthAsync_ForAContainerWithoutAHealthcheck_SaysSo()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("healthnone", "busybox:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var report = await Client.Diagnostics.GetHealthAsync(id, cancellation.Token);

            Assert.False(report.HasHealthcheck);
            Assert.False(report.IsHealthy);
            Assert.Null(report.Status);
            Assert.Empty(report.RecentLogs);
            Assert.Contains("no healthcheck", report.Interpretation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task DiagnoseAsync_AggregatesEverySubReport()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("diagnose", "busybox:latest", "sh", "-c", StatsTests.BusyLoop);
        spec.Limits = new ResourceLimits
        {
            Cpus = 0.1,
            MemoryBytes = ResourceLimits.Megabytes(128),
            MemorySwapBytes = ResourceLimits.Megabytes(128),
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var containerId = id;

            var report = await Poll.UntilAsync(
                token => Client.Diagnostics.DiagnoseAsync(containerId, token),
                sample => sample.CpuThrottling.HasLiveData && sample.Memory.HasLiveData,
                TimeSpan.FromSeconds(60), "live diagnostics for the running container",
                TimeSpan.FromSeconds(2), cancellation.Token);

            Assert.Equal(id, report.ContainerId);
            Assert.Equal(spec.Name, report.ContainerName);
            Assert.True(report.IsRunning);
            Assert.Equal("running", report.Status);
            Assert.NotNull(report.CpuThrottling);
            Assert.NotNull(report.Memory);
            Assert.NotNull(report.Oom);
            Assert.NotNull(report.Health);
            Assert.False(report.Oom.WasOomKilled);
            Assert.False(report.Health.HasHealthcheck);
            Assert.NotEmpty(report.Summary);
            Assert.Equal(ResourceLimits.Megabytes(128), report.Memory.LimitBytes);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }
}
