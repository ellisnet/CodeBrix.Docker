using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>
/// Creates, discovers and tears down Redis instances. Labels are the database: nothing here keeps a
/// side-car state file, and an instance created in an earlier run is rediscovered from its labels.
/// </summary>
public interface IRedisTopologyService
{
    /// <summary>Gets the thirteen approved topologies.</summary>
    IReadOnlyList<TopologyDescriptor> Catalog { get; }

    /// <summary>Gets one topology's descriptor.</summary>
    /// <param name="id">The topology.</param>
    /// <returns>The descriptor.</returns>
    TopologyDescriptor Describe(TopologyId id);

    /// <summary>Checks a request without touching the daemon.</summary>
    /// <param name="request">The request to check.</param>
    /// <returns>The problems found; empty means the request is valid.</returns>
    IReadOnlyList<string> Validate(TopologyRequest request);

    /// <summary>Works out which host ports a request would use, without reserving them.</summary>
    /// <param name="request">The request to preview.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The plan.</returns>
    Task<PortPlan> PreviewPortsAsync(TopologyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an instance, rolling back everything it made if any step fails.</summary>
    /// <param name="request">What to create.</param>
    /// <param name="progress">Receives one report per step.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The created instance.</returns>
    Task<TopologyInstance> CreateAsync(TopologyRequest request,
        IProgress<TopologyProgress> progress = null, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds every instance on the daemon from its labels.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The instances, newest first.</returns>
    Task<IReadOnlyList<TopologyInstance>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Rebuilds one instance from its labels.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The instance, or null when nothing carries the id.</returns>
    Task<TopologyInstance> RefreshAsync(string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Starts every container of an instance, in dependency order.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the instance is running.</returns>
    Task StartAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Stops every container of an instance.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the instance is stopped.</returns>
    Task StopAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Stops and restarts every container of an instance.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the instance is running again.</returns>
    Task RestartAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>Removes every container, volume and network belonging to an instance.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="progress">Receives one report per step.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when nothing carrying the id is left.</returns>
    /// <remarks>Teardown is idempotent: a missing resource is not an error.</remarks>
    Task DestroyAsync(string instanceId, IProgress<TopologyProgress> progress = null,
        CancellationToken cancellationToken = default);
}
