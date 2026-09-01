using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.DockerManagement.Topologies;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.Services;

/// <summary>
/// The shared, observable snapshot every section reads. One refresh asks the daemon for
/// everything the shell and the eight sections need, so the sections never race each other
/// for the same daemon call. <see cref="Changed"/> is raised on whatever thread the refresh
/// finished on; subscribers marshal for themselves.
/// </summary>
public sealed class AppState
{
    private readonly IDockerManager _docker;
    private readonly IRedisTopologyService _topologies;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the shared snapshot over the Docker facade and the topology service.</summary>
    /// <param name="docker">The Docker facade every section reads through.</param>
    /// <param name="topologies">The topology service that discovers managed instances.</param>
    public AppState(IDockerManager docker, IRedisTopologyService topologies)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _topologies = topologies ?? throw new ArgumentNullException(nameof(topologies));
    }

    /// <summary>Raised after every refresh, successful or not.</summary>
    public event Action Changed;

    /// <summary>The daemon's own description, or null before the first successful refresh.</summary>
    public DaemonInfo Daemon { get; private set; }

    /// <summary>What the daemon's storage is holding, or null before the first refresh.</summary>
    public DaemonDiskUsage DiskUsage { get; private set; }

    /// <summary>Every container on the daemon, running or not.</summary>
    public IReadOnlyList<ContainerInfo> Containers { get; private set; } = [];

    /// <summary>Every image on the daemon.</summary>
    public IReadOnlyList<ImageInfo> Images { get; private set; } = [];

    /// <summary>Every network on the daemon.</summary>
    public IReadOnlyList<NetworkInfo> Networks { get; private set; } = [];

    /// <summary>Every volume on the daemon.</summary>
    public IReadOnlyList<VolumeInfo> Volumes { get; private set; } = [];

    /// <summary>The Redis instances this tool created, as discovered from container labels.</summary>
    public IReadOnlyList<TopologyInstance> Instances { get; private set; } = [];

    /// <summary>The advisor's findings across every container on the daemon.</summary>
    public IReadOnlyList<AdvisorFindingInfo> Findings { get; private set; } = [];

    /// <summary>Whether the last refresh reached the daemon.</summary>
    public bool IsDaemonReachable { get; private set; }

    /// <summary>The message from the last failed refresh, or null when the last one succeeded.</summary>
    public string LastError { get; private set; }

    /// <summary>When the last refresh finished.</summary>
    public DateTimeOffset LastRefreshed { get; private set; }

    /// <summary>How many of <see cref="Containers"/> are running.</summary>
    public int RunningContainerCount
    {
        get
        {
            var count = 0;
            foreach (var container in Containers)
            {
                if (container.IsRunning) { count++; }
            }
            return count;
        }
    }

    /// <summary>How many of <see cref="Instances"/> have every node running.</summary>
    public int RunningInstanceCount
    {
        get
        {
            var count = 0;
            foreach (var instance in Instances)
            {
                if (instance.State == InstanceState.Running) { count++; }
            }
            return count;
        }
    }

    /// <summary>Finds a discovered instance by its id.</summary>
    /// <param name="instanceId">The instance id to look for.</param>
    /// <returns>The instance, or null when it is not in the current snapshot.</returns>
    public TopologyInstance FindInstance(string instanceId)
    {
        foreach (var instance in Instances)
        {
            if (string.Equals(instance.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return instance;
            }
        }
        return null;
    }

    /// <summary>
    /// Asks the daemon for everything at once and replaces the snapshot. Never throws: a
    /// failure leaves <see cref="IsDaemonReachable"/> false and the message in
    /// <see cref="LastError"/>, and <see cref="Changed"/> is raised either way.
    /// </summary>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>A task that completes when the snapshot has been replaced.</returns>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var daemon = await _docker.GetDaemonInfoAsync(cancellationToken).ConfigureAwait(false);
            Daemon = daemon;
            IsDaemonReachable = daemon is not null && daemon.IsReachable;

            if (!IsDaemonReachable)
            {
                LastError = "The Docker daemon did not answer at " + _docker.Endpoint + ".";
                LastRefreshed = DateTimeOffset.Now;
                return;
            }

            Containers = await _docker.ListContainersAsync(true, cancellationToken)
                .ConfigureAwait(false);
            Images = await _docker.ListImagesAsync(false, cancellationToken).ConfigureAwait(false);
            Networks = await _docker.ListNetworksAsync(cancellationToken).ConfigureAwait(false);
            Volumes = await _docker.ListVolumesAsync(cancellationToken).ConfigureAwait(false);
            DiskUsage = await _docker.GetDiskUsageAsync(cancellationToken).ConfigureAwait(false);
            Instances = await _topologies.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            Findings = await _docker.AdviseAllContainersAsync(cancellationToken)
                .ConfigureAwait(false);

            LastError = null;
            LastRefreshed = DateTimeOffset.Now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            IsDaemonReachable = false;
            LastError = exception.Message;
            LastRefreshed = DateTimeOffset.Now;
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }
}
