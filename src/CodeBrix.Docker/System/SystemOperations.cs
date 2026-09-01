using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Daemon-level operations: connectivity, version, host configuration, disk usage and the event stream.
/// </summary>
public sealed class SystemOperations
{
    private readonly DockerApiClient _api;

    internal SystemOperations(DockerApiClient api) => _api = api;

    /// <summary>
    /// Checks whether the daemon is reachable.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the daemon answered <c>GET /_ping</c>; otherwise <see langword="false"/>.</returns>
    public Task<bool> PingAsync(CancellationToken cancellationToken = default) =>
        _api.TryGetAsync("_ping", cancellationToken);

    /// <summary>
    /// Gets the daemon and Engine API versions.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The version information.</returns>
    public Task<DockerVersionInfo> GetVersionAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<DockerVersionInfo>("version", cancellationToken);

    /// <summary>
    /// Gets the daemon's host configuration, including the cgroup version and driver that determine
    /// how resource limits behave.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The daemon information.</returns>
    public Task<DockerSystemInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
        _api.GetAsync<DockerSystemInfo>("info", cancellationToken);

    /// <summary>
    /// Gets disk-usage totals for images, containers, volumes and the build cache.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The aggregated disk usage.</returns>
    public async Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default)
    {
        var response = await _api.GetAsync<DiskUsageResponse>("system/df", cancellationToken).ConfigureAwait(false);

        var images = response.Images ?? [];
        var containers = response.Containers ?? [];
        var volumes = response.Volumes ?? [];
        var buildCache = response.BuildCache ?? [];

        long volumesSize = 0;
        var reclaimableVolumes = 0;
        foreach (var volume in volumes)
        {
            var size = volume.UsageData?.Size ?? 0;
            if (size > 0)
            {
                volumesSize += size;
            }

            if ((volume.UsageData?.RefCount ?? 0) == 0)
            {
                reclaimableVolumes++;
            }
        }

        long buildCacheSize = 0;
        long reclaimableBuildCache = 0;
        foreach (var record in buildCache)
        {
            if (record.Shared)
            {
                continue;
            }

            buildCacheSize += record.Size;
            if (!record.InUse)
            {
                reclaimableBuildCache += record.Size;
            }
        }

        return new DiskUsageInfo
        {
            LayersSizeBytes = response.LayersSize,
            ImageCount = images.Count,
            ImagesSizeBytes = images.Sum(i => i.Size),
            ReclaimableImageCount = images.Count(i => i.Containers <= 0),
            ContainerCount = containers.Count,
            ContainersSizeBytes = containers.Sum(c => c.SizeRw),
            VolumeCount = volumes.Count,
            VolumesSizeBytes = volumesSize,
            ReclaimableVolumeCount = reclaimableVolumes,
            BuildCacheSizeBytes = buildCacheSize,
            ReclaimableBuildCacheBytes = reclaimableBuildCache,
        };
    }

    /// <summary>
    /// Streams daemon events as they happen. The sequence ends when <paramref name="cancellationToken"/>
    /// is cancelled or the connection closes.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that stops the stream.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    public IAsyncEnumerable<DockerEvent> StreamEventsAsync(CancellationToken cancellationToken = default) =>
        _api.GetJsonLinesAsync<DockerEvent>("events", cancellationToken);

    /// <summary>
    /// Streams daemon events filtered by the daemon-side <c>filters</c> parameter.
    /// </summary>
    /// <param name="type">The object type to filter on, for example <c>container</c>. Optional.</param>
    /// <param name="containerIdOrName">A container to filter on. Optional.</param>
    /// <param name="cancellationToken">A cancellation token that stops the stream.</param>
    /// <returns>An asynchronous sequence of events.</returns>
    public IAsyncEnumerable<DockerEvent> StreamEventsAsync(string type, string containerIdOrName,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder();
        if (!string.IsNullOrWhiteSpace(type))
        {
            query.AddFilter("type", type);
        }

        if (!string.IsNullOrWhiteSpace(containerIdOrName))
        {
            query.AddFilter("container", containerIdOrName);
        }

        return _api.GetJsonLinesAsync<DockerEvent>(query.AppendTo("events"), cancellationToken);
    }

    /// <summary>
    /// Verifies that the daemon is running Linux containers. The cgroup-based diagnostics and resource
    /// limits in this library only apply to a Linux daemon.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the check passes.</returns>
    /// <exception cref="DockerException">The daemon is not in Linux container mode.</exception>
    public async Task EnsureLinuxDaemonAsync(CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(info.OsType, "linux", StringComparison.OrdinalIgnoreCase))
        {
            throw new DockerException(
                $"CodeBrix.Docker requires a Linux Docker daemon, but the daemon reports OSType '{info.OsType ?? "unknown"}'. " +
                "Switch Docker Desktop to Linux containers.");
        }
    }
}
