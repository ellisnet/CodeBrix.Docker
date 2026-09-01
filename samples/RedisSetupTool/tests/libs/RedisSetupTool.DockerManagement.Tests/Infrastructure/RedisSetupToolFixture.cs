using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>
/// Owns the facade every test class shares and guarantees the daemon is left as it was found.
/// </summary>
/// <remarks>
/// Everything the suite creates carries an extra <c>codebrix.redissetup.tests</c> label, and the
/// sweep matches ONLY that label. Sweeping by the instance label's presence would destroy instances
/// created by hand, and the library's own <c>codebrix.docker.tests</c> label is never touched.
/// </remarks>
public sealed class RedisSetupToolFixture : IAsyncLifetime
{
    /// <summary>The label name every resource the suite creates carries.</summary>
    public const string TestLabelName = "codebrix.redissetup.tests";

    /// <summary>The label value every resource the suite creates carries.</summary>
    public const string TestLabelValue = "true";

    /// <summary>The name prefix the suite's own scratch containers carry.</summary>
    public const string NamePrefix = "redissetup-test-";

    /// <summary>The images the suite runs containers from.</summary>
    public static readonly string[] BaseImages = ["redis:8-alpine", "alpine:latest"];

    private DockerManager _docker;

    /// <summary>Gets the shared facade.</summary>
    public DockerManager Docker =>
        _docker ?? throw new InvalidOperationException("The fixture has not been initialized.");

    /// <summary>Gets the topology service built over the shared facade.</summary>
    public IRedisTopologyService Topologies { get; private set; }

    /// <summary>Gets the allocator the topology service uses.</summary>
    public IHostPortAllocator Ports { get; private set; }

    /// <summary>Gets a filter matching the suite's own resources.</summary>
    public static Dictionary<string, string> TestLabelFilter =>
        new(StringComparer.Ordinal) { [TestLabelName] = TestLabelValue };

    /// <summary>Builds a unique, prefixed scratch name.</summary>
    /// <param name="role">What the resource is for.</param>
    /// <returns>The name.</returns>
    public static string NewName(string role) =>
        NamePrefix + role + "-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Builds a topology request already carrying the suite's own label.</summary>
    /// <param name="topologyId">The topology to create.</param>
    /// <param name="instanceName">The friendly name; null generates one.</param>
    /// <returns>The request.</returns>
    public static TopologyRequest Request(TopologyId topologyId, string instanceName = null)
    {
        var request = new TopologyRequest
        {
            TopologyId = topologyId,
            InstanceName = instanceName ?? NewName(topologyId.ToString().ToLowerInvariant()),
        };

        request.ExtraLabels[TestLabelName] = TestLabelValue;
        return request;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _docker = new DockerManager();
        Ports = new HostPortAllocator(_docker);
        Topologies = new RedisTopologyService(_docker, Ports);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        if (!await _docker.PingAsync(cancellation.Token))
        {
            throw new InvalidOperationException("The Docker daemon did not answer a ping.");
        }

        //A previous aborted run must not influence this one.
        await SweepAsync(cancellation.Token);

        foreach (var image in BaseImages)
        {
            await _docker.PullImageAsync(image, progress: null, cancellation.Token);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_docker is null)
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await SweepAsync(cancellation.Token);
        }
        catch (Exception)
        {
            //Cleanup is best effort; a failure here must not mask a test result.
        }

        _docker.Dispose();
        _docker = null;
    }

    /// <summary>Removes an instance, ignoring failures.</summary>
    /// <param name="instanceId">The instance id; null is ignored.</param>
    /// <returns>A task that completes when the attempt is over.</returns>
    public async Task DestroyQuietlyAsync(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await Topologies.DestroyAsync(instanceId, progress: null, cancellation.Token);
        }
        catch (Exception)
        {
            //The sweep is the backstop.
        }
    }

    /// <summary>Force-removes a container, ignoring failures.</summary>
    /// <param name="idOrName">The container; null is ignored.</param>
    /// <returns>A task that completes when the attempt is over.</returns>
    public async Task RemoveContainerQuietlyAsync(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Docker.RemoveContainerAsync(idOrName, force: true, removeVolumes: false,
                cancellation.Token);
        }
        catch (Exception)
        {
            //The sweep is the backstop.
        }
    }

    /// <summary>Removes every resource carrying the suite's own label.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the sweep is over.</returns>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        foreach (var container in await Docker.ListContainersAsync(true, cancellationToken))
        {
            if (IsOurs(container.Labels) || container.Name.StartsWith(NamePrefix,
                    StringComparison.Ordinal))
            {
                try
                {
                    await Docker.RemoveContainerAsync(container.Id, force: true,
                        removeVolumes: false, cancellationToken);
                }
                catch (DockerManagementException)
                {
                    //Keep sweeping; one stubborn container must not strand the rest.
                }
            }
        }

        foreach (var volume in await Docker.ListVolumesAsync(cancellationToken))
        {
            if (IsOurs(volume.Labels))
            {
                try
                {
                    await Docker.RemoveVolumeAsync(volume.Name, force: true, cancellationToken);
                }
                catch (DockerManagementException)
                {
                    //Keep sweeping.
                }
            }
        }

        foreach (var network in await Docker.ListNetworksAsync(cancellationToken))
        {
            if (IsOurs(network.Labels))
            {
                try
                {
                    await Docker.RemoveNetworkAsync(network.Id, cancellationToken);
                }
                catch (DockerManagementException)
                {
                    //Keep sweeping.
                }
            }
        }
    }

    private static bool IsOurs(IReadOnlyDictionary<string, string> labels) =>
        labels is not null && labels.TryGetValue(TestLabelName, out var value)
        && string.Equals(value, TestLabelValue, StringComparison.Ordinal);
}
