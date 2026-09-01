using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Network lifecycle and attachment operations.
/// </summary>
/// <remarks>
/// A user-defined network is what gives containers name-based service discovery: containers on the
/// same user-defined network resolve one another by container name and by any alias given at attach
/// time, which the default <c>bridge</c> network does not provide.
/// </remarks>
public sealed class NetworkOperations
{
    private readonly DockerApiClient _api;

    internal NetworkOperations(DockerApiClient api) => _api = api;

    /// <summary>
    /// Creates a network.
    /// </summary>
    /// <param name="name">The network name.</param>
    /// <param name="driver">The driver, <c>bridge</c> by default.</param>
    /// <param name="labels">Optional labels to attach to the network.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new network's id.</returns>
    /// <exception cref="DockerApiException">A network with that name already exists.</exception>
    public async Task<string> CreateAsync(string name, string driver = "bridge",
        IDictionary<string, string> labels = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var request = new NetworkCreateRequest
        {
            Name = name,
            Driver = string.IsNullOrWhiteSpace(driver) ? "bridge" : driver,
            Labels = labels is { Count: > 0 }
                ? new Dictionary<string, string>(labels, StringComparer.Ordinal)
                : null,
        };

        var response = await _api
            .PostAsync<NetworkCreateResponse>("networks/create", request, cancellationToken)
            .ConfigureAwait(false);

        return response.Id
               ?? throw new DockerException("The Docker daemon did not return an id for the new network.");
    }

    /// <summary>
    /// Lists the networks known to the daemon.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The networks, including the predefined <c>bridge</c>, <c>host</c> and <c>none</c>.</returns>
    public Task<IReadOnlyList<NetworkSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        ListAsync(labelFilters: null, cancellationToken);

    /// <summary>
    /// Lists the networks carrying the given labels.
    /// </summary>
    /// <param name="labelFilters">
    /// Label filters. An entry with an empty value matches the label's presence.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching networks.</returns>
    public async Task<IReadOnlyList<NetworkSummary>> ListAsync(IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddLabelFilters(labelFilters);

        var networks = await _api
            .GetAsync<List<NetworkSummary>>(query.AppendTo("networks"), cancellationToken)
            .ConfigureAwait(false);

        return networks;
    }

    /// <summary>
    /// Gets the full description of a network, including the containers attached to it.
    /// </summary>
    /// <param name="idOrName">The network id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The inspect result.</returns>
    /// <exception cref="DockerApiException">No such network exists.</exception>
    public Task<NetworkInspectResult> InspectAsync(string idOrName, CancellationToken cancellationToken = default) =>
        _api.GetAsync<NetworkInspectResult>($"networks/{Reference(idOrName)}", cancellationToken);

    /// <summary>
    /// Removes a network. The network must have no containers attached.
    /// </summary>
    /// <param name="idOrName">The network id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the network is gone.</returns>
    /// <exception cref="DockerApiException">
    /// No such network exists, the network still has endpoints, or it is one of the predefined
    /// networks.
    /// </exception>
    public Task RemoveAsync(string idOrName, CancellationToken cancellationToken = default) =>
        _api.DeleteAsync($"networks/{Reference(idOrName)}", cancellationToken);

    /// <summary>
    /// Attaches a container to a network.
    /// </summary>
    /// <param name="network">The network id or name.</param>
    /// <param name="container">The container id or name.</param>
    /// <param name="aliases">
    /// Extra DNS names the container answers to on this network. Other containers on the same
    /// network resolve the container by its name and by each of these aliases.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is attached.</returns>
    /// <exception cref="DockerApiException">The network or the container does not exist.</exception>
    public Task ConnectAsync(string network, string container, IReadOnlyList<string> aliases = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var request = new NetworkConnectRequest
        {
            Container = container.TrimStart('/'),
            EndpointConfig = aliases is { Count: > 0 }
                ? new EndpointConfigWire { Aliases = aliases.ToArray() }
                : null,
        };

        return _api.PostAsync($"networks/{Reference(network)}/connect", request, cancellationToken);
    }

    /// <summary>
    /// Detaches a container from a network.
    /// </summary>
    /// <param name="network">The network id or name.</param>
    /// <param name="container">The container id or name.</param>
    /// <param name="force">When <see langword="true"/>, forces the endpoint to be removed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is detached.</returns>
    /// <exception cref="DockerApiException">The network or the container does not exist.</exception>
    public Task DisconnectAsync(string network, string container, bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var request = new NetworkDisconnectRequest
        {
            Container = container.TrimStart('/'),
            Force = force,
        };

        return _api.PostAsync($"networks/{Reference(network)}/disconnect", request, cancellationToken);
    }

    /// <summary>
    /// Prunes every user-defined network that has no container attached.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(CancellationToken cancellationToken = default) =>
        PruneAsync(labelFilters: null, cancellationToken);

    /// <summary>
    /// Prunes the unused user-defined networks carrying the given labels.
    /// </summary>
    /// <param name="labelFilters">
    /// Label filters restricting what is pruned. An entry with an empty value matches the label's
    /// presence. Supplying filters is the safe way to clean up networks a test or tool created.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddLabelFilters(labelFilters);
        return _api.PostAsync<NetworksPruneResponse>(query.AppendTo("networks/prune"), body: null,
            cancellationToken, applyTimeout: false);
    }

    private static string Reference(string idOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrName);
        return Uri.EscapeDataString(idOrName);
    }
}
