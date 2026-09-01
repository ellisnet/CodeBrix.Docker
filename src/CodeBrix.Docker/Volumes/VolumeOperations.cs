using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Volume lifecycle operations.
/// </summary>
/// <remarks>
/// A named volume keeps a container's writes out of its copy-on-write layer, which is both faster
/// and the only way the data survives the container.
/// </remarks>
public sealed class VolumeOperations
{
    private readonly DockerApiClient _api;

    internal VolumeOperations(DockerApiClient api) => _api = api;

    /// <summary>
    /// Creates a volume.
    /// </summary>
    /// <param name="name">
    /// The volume name. When omitted the daemon generates one and the volume is anonymous, which
    /// means <see cref="PruneAsync(CancellationToken)"/> will reclaim it once nothing uses it.
    /// </param>
    /// <param name="labels">Optional labels to attach to the volume.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volume's name.</returns>
    /// <remarks>Creating a volume that already exists succeeds and returns the existing volume.</remarks>
    public async Task<string> CreateAsync(string name = null, IDictionary<string, string> labels = null,
        CancellationToken cancellationToken = default)
    {
        var request = new VolumeCreateRequest
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Driver = "local",
            Labels = labels is { Count: > 0 }
                ? new Dictionary<string, string>(labels, StringComparer.Ordinal)
                : null,
        };

        var created = await _api
            .PostAsync<VolumeInspectResult>("volumes/create", request, cancellationToken)
            .ConfigureAwait(false);

        return created.Name.Length > 0
            ? created.Name
            : throw new DockerException("The Docker daemon did not return a name for the new volume.");
    }

    /// <summary>
    /// Lists the volumes known to the daemon.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volumes.</returns>
    public Task<IReadOnlyList<VolumeSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        ListAsync(labelFilters: null, cancellationToken);

    /// <summary>
    /// Lists the volumes carrying the given labels.
    /// </summary>
    /// <param name="labelFilters">
    /// Label filters. An entry with an empty value matches the label's presence.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching volumes.</returns>
    public async Task<IReadOnlyList<VolumeSummary>> ListAsync(IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddLabelFilters(labelFilters);

        // This endpoint wraps its array in an object, unlike the container, image and network lists.
        var response = await _api
            .GetAsync<VolumeListResponse>(query.AppendTo("volumes"), cancellationToken)
            .ConfigureAwait(false);

        return response.Volumes ?? [];
    }

    /// <summary>
    /// Gets the full description of a volume.
    /// </summary>
    /// <param name="name">The volume name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The inspect result.</returns>
    /// <exception cref="DockerApiException">No such volume exists.</exception>
    public Task<VolumeInspectResult> InspectAsync(string name, CancellationToken cancellationToken = default) =>
        _api.GetAsync<VolumeInspectResult>($"volumes/{Reference(name)}", cancellationToken);

    /// <summary>
    /// Removes a volume and the data it holds.
    /// </summary>
    /// <param name="name">The volume name.</param>
    /// <param name="force">
    /// When <see langword="true"/>, removing a volume that does not exist succeeds instead of
    /// failing. It does not remove a volume that is still in use.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the volume is gone.</returns>
    /// <exception cref="DockerApiException">
    /// No such volume exists, or a container is still using it.
    /// </exception>
    public Task RemoveAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddIfTrue("force", force);
        return _api.DeleteAsync(query.AppendTo($"volumes/{Reference(name)}"), cancellationToken);
    }

    /// <summary>
    /// Prunes the anonymous volumes no container is using.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    /// <remarks>
    /// Named volumes are deliberately left alone: the daemon only considers them when the <c>all</c>
    /// filter is set, which <see cref="PruneAsync(IDictionary{string, string}, CancellationToken)"/>
    /// does once label filters narrow the sweep.
    /// </remarks>
    public Task PruneAsync(CancellationToken cancellationToken = default) =>
        _api.PostAsync<VolumesPruneResponse>("volumes/prune", body: null, cancellationToken,
            applyTimeout: false);

    /// <summary>
    /// Prunes the unused volumes carrying the given labels, named volumes included.
    /// </summary>
    /// <param name="labelFilters">
    /// Label filters restricting what is pruned. An entry with an empty value matches the label's
    /// presence. Passing no filters falls back to pruning anonymous volumes only, since an unfiltered
    /// sweep over named volumes destroys data.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(IDictionary<string, string> labelFilters,
        CancellationToken cancellationToken = default)
    {
        if (labelFilters is not { Count: > 0 })
        {
            return PruneAsync(cancellationToken);
        }

        var query = new QueryStringBuilder()
            .AddFilter("all", "true")
            .AddLabelFilters(labelFilters);

        return _api.PostAsync<VolumesPruneResponse>(query.AppendTo("volumes/prune"), body: null,
            cancellationToken, applyTimeout: false);
    }

    private static string Reference(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Uri.EscapeDataString(name);
    }
}
