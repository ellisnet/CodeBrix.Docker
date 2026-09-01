using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class ResourceLimitTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task CreateAsync_AppliesEveryConfiguredResourceLimit()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("limits", "alpine:latest", "sleep", "300");
        spec.Limits = new ResourceLimits
        {
            Cpus = 0.25,
            CpusetCpus = "0",
            CpuShares = 512,
            MemoryBytes = ResourceLimits.Megabytes(128),
            MemoryReservationBytes = ResourceLimits.Megabytes(64),
            MemorySwapBytes = ResourceLimits.Megabytes(128),
            PidsLimit = 100,
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var hostConfig = (await Client.Containers.InspectAsync(id, cancellation.Token)).HostConfig;

            Assert.NotNull(hostConfig);
            Assert.Equal(250_000_000, hostConfig.NanoCpus);
            Assert.Equal(0.25, hostConfig.Cpus);
            Assert.Equal("0", hostConfig.CpusetCpus);
            Assert.Equal(512, hostConfig.CpuShares);
            Assert.Equal(134_217_728, hostConfig.Memory);
            Assert.Equal(67_108_864, hostConfig.MemoryReservation);
            Assert.Equal(134_217_728, hostConfig.MemorySwap);
            Assert.Equal(100, hostConfig.PidsLimit);
            Assert.True(hostConfig.HasCpuLimit);
            Assert.True(hostConfig.HasMemoryLimit);
            Assert.True(hostConfig.IsSwapDisabled);
            Assert.False(hostConfig.Privileged);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task UpdateResourcesAsync_RaisesTheCpuQuotaOfARunningContainer()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("cpuupdate", "alpine:latest", "sleep", "300");
        spec.Limits = new ResourceLimits { Cpus = 0.25 };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);
            var before = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.Equal(250_000_000, before.HostConfig?.NanoCpus);

            await Client.Containers.UpdateResourcesAsync(id, new ResourceLimits { Cpus = 0.5 },
                cancellation.Token);

            var after = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.Equal(500_000_000, after.HostConfig?.NanoCpus);
            Assert.Equal(0.5, after.HostConfig?.Cpus);
            Assert.True(after.IsRunning, "The container should be retuned without a restart.");

            var quota = await Client.Containers.ExecAsync(id, ["cat", "/sys/fs/cgroup/cpu.max"],
                cancellationToken: cancellation.Token);
            Assert.Equal(0, quota.ExitCode);
            Assert.StartsWith("50000 ", quota.Stdout.Trim(), StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task UpdateResourcesAsync_RaisesTheMemoryLimitOfARunningContainer()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("memupdate", "alpine:latest", "sleep", "300");
        spec.Limits = new ResourceLimits
        {
            MemoryBytes = ResourceLimits.Megabytes(128),
            MemorySwapBytes = ResourceLimits.Megabytes(128),
        };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Client.Containers.UpdateResourcesAsync(id, new ResourceLimits
            {
                MemoryBytes = ResourceLimits.Megabytes(256),
                MemorySwapBytes = ResourceLimits.Megabytes(256),
            }, cancellation.Token);

            var after = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.Equal(268_435_456, after.HostConfig?.Memory);
            Assert.Equal(268_435_456, after.HostConfig?.MemorySwap);
            Assert.True(after.IsRunning);

            var cgroupLimit = await Client.Containers.ExecAsync(id, ["cat", "/sys/fs/cgroup/memory.max"],
                cancellationToken: cancellation.Token);
            Assert.Equal("268435456", cgroupLimit.Stdout.Trim());
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task UpdateResourcesAsync_WithNoLimitsSet_IsRejected()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var spec = fixture.Spec("emptyupdate", "alpine:latest", "sleep", "60");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await Assert.ThrowsAsync<ArgumentException>(
                () => Client.Containers.UpdateResourcesAsync(id, new ResourceLimits(), cancellation.Token));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task PidsLimit_StopsTheContainerFromSpawningMoreProcessesThanAllowed()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("pids", "busybox:latest", "sleep", "300");
        spec.Limits = new ResourceLimits { PidsLimit = 10 };
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var configured = await Client.Containers.ExecAsync(id, ["cat", "/sys/fs/cgroup/pids.max"],
                cancellationToken: cancellation.Token);
            Assert.Equal(0, configured.ExitCode);
            Assert.Equal("10", configured.Stdout.Trim());

            // Ask for far more concurrent processes than the limit allows; the kernel must refuse.
            var storm = await Client.Containers.ExecAsync(id,
                ["sh", "-c", "i=0; while [ $i -lt 40 ]; do sleep 5 & i=$((i+1)); done; echo requested=$i"],
                cancellationToken: cancellation.Token);

            var combined = storm.Stdout + storm.Stderr;
            Assert.True(
                combined.Contains("can't fork", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("Resource temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                || storm.ExitCode != 0,
                $"The PID limit should have refused the extra processes. Output was: {combined}");
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task Container_ThatExceedsItsMemoryLimit_IsOomKilledWithExitCode137()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var spec = OomSpecs.MemoryHog(fixture);
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            var exitCode = await Client.Containers.WaitForExitAsync(id, cancellation.Token);
            Assert.Equal(137, exitCode);

            var inspect = await Client.Containers.InspectAsync(id, cancellation.Token);
            Assert.True(inspect.State?.OomKilled, "The kernel should record the OOM kill.");
            Assert.Equal(137, inspect.State?.ExitCode);
            Assert.False(inspect.IsRunning);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }
}

/// <summary>Shared container specs for the out-of-memory scenarios.</summary>
internal static class OomSpecs
{
    /// <summary>
    /// A container that is guaranteed to be OOM-killed: it fills a tmpfs larger than its own memory
    /// limit, and tmpfs pages are charged to the container and cannot be reclaimed with swap disabled.
    /// </summary>
    public static ContainerSpec MemoryHog(DockerTestFixture fixture)
    {
        var spec = fixture.Spec("oom", "alpine:latest", "dd", "if=/dev/zero", "of=/hog/fill", "bs=1M",
            "count=200");
        spec.Limits = new ResourceLimits
        {
            MemoryBytes = ResourceLimits.Megabytes(64),
            MemorySwapBytes = ResourceLimits.Megabytes(64),
        };
        spec.Mounts.Add(MountSpec.Tmpfs("/hog", ResourceLimits.Megabytes(512)));
        return spec;
    }
}
