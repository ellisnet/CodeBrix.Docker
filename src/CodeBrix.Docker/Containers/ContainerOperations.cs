using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Container lifecycle, resource and inspection operations.
/// </summary>
public sealed class ContainerOperations
{
    private readonly DockerApiClient _api;

    internal ContainerOperations(DockerApiClient api) => _api = api;

    // ---------------------------------------------------------------------------------------
    // Listing and inspection
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Lists containers.
    /// </summary>
    /// <param name="all">When <see langword="true"/>, includes stopped containers.</param>
    /// <param name="labelFilters">
    /// Optional label filters. An entry with an empty value matches the label's presence.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching containers.</returns>
    public async Task<IReadOnlyList<ContainerSummary>> ListAsync(bool all = false,
        IDictionary<string, string> labelFilters = null, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder()
            .AddIfTrue("all", all)
            .AddLabelFilters(labelFilters);

        var containers = await _api
            .GetAsync<List<ContainerSummary>>(query.AppendTo("containers/json"), cancellationToken)
            .ConfigureAwait(false);

        return containers;
    }

    /// <summary>
    /// Gets the full description of a container.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The inspect result.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container exists.</exception>
    public Task<ContainerInspectResult> InspectAsync(string idOrName, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ContainerInspectResult>($"containers/{Reference(idOrName)}/json", cancellationToken);

    // ---------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a container without starting it.
    /// </summary>
    /// <param name="spec">The container specification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new container's id.</returns>
    /// <exception cref="DockerImageNotFoundException">The image is not available locally.</exception>
    public async Task<string> CreateAsync(ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.Image))
        {
            throw new ArgumentException("ContainerSpec.Image is required.", nameof(spec));
        }

        var query = new QueryStringBuilder().Add("name", spec.Name);
        var request = BuildCreateRequest(spec);

        var response = await _api
            .PostAsync<ContainerCreateResponse>(query.AppendTo("containers/create"), request, cancellationToken)
            .ConfigureAwait(false);

        return response.Id
               ?? throw new DockerException("The Docker daemon did not return an id for the new container.");
    }

    /// <summary>
    /// Starts a container. Starting an already-running container succeeds silently.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has started the container.</returns>
    public Task StartAsync(string idOrName, CancellationToken cancellationToken = default) =>
        _api.PostAsync($"containers/{Reference(idOrName)}/start", body: null, cancellationToken);

    /// <summary>
    /// Creates a container and starts it.
    /// </summary>
    /// <param name="spec">The container specification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new container's id.</returns>
    public async Task<string> RunAsync(ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        var id = await CreateAsync(spec, cancellationToken).ConfigureAwait(false);
        await StartAsync(id, cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// Stops a container, sending SIGTERM and then SIGKILL after the grace period.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeoutSeconds">Seconds to wait before escalating to SIGKILL.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container has stopped.</returns>
    public Task StopAsync(string idOrName, int timeoutSeconds = 10, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().Add("t", timeoutSeconds);
        return _api.PostAsync(query.AppendTo($"containers/{Reference(idOrName)}/stop"), body: null,
            cancellationToken, applyTimeout: false);
    }

    /// <summary>
    /// Restarts a container.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeoutSeconds">Seconds to wait before escalating to SIGKILL.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container has restarted.</returns>
    public Task RestartAsync(string idOrName, int timeoutSeconds = 10, CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().Add("t", timeoutSeconds);
        return _api.PostAsync(query.AppendTo($"containers/{Reference(idOrName)}/restart"), body: null,
            cancellationToken, applyTimeout: false);
    }

    /// <summary>
    /// Sends a signal to a container's main process.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="signal">The signal name, for example <c>SIGKILL</c> or <c>SIGTERM</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has delivered the signal.</returns>
    public Task KillAsync(string idOrName, string signal = "SIGKILL", CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().Add("signal", signal);
        return _api.PostAsync(query.AppendTo($"containers/{Reference(idOrName)}/kill"), body: null, cancellationToken);
    }

    /// <summary>
    /// Removes a container.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="force">When <see langword="true"/>, kills the container first if it is running.</param>
    /// <param name="removeVolumes">When <see langword="true"/>, also removes anonymous volumes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is gone.</returns>
    public Task RemoveAsync(string idOrName, bool force = false, bool removeVolumes = false,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder()
            .AddIfTrue("force", force)
            .AddIfTrue("v", removeVolumes);

        return _api.DeleteAsync(query.AppendTo($"containers/{Reference(idOrName)}"), cancellationToken);
    }

    /// <summary>
    /// Retunes a running container's resource limits without restarting it.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="limits">The limits to apply. Unset properties are left untouched.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has applied the new limits.</returns>
    /// <exception cref="ArgumentException"><paramref name="limits"/> sets nothing.</exception>
    public Task UpdateResourcesAsync(string idOrName, ResourceLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.IsEmpty)
        {
            throw new ArgumentException("No resource limits were set to update.", nameof(limits));
        }

        var request = new ContainerUpdateRequest
        {
            NanoCpus = limits.ToNanoCpus(),
            CpusetCpus = limits.CpusetCpus,
            CpuShares = limits.CpuShares,
            Memory = limits.MemoryBytes,
            MemoryReservation = limits.MemoryReservationBytes,
            MemorySwap = limits.MemorySwapBytes,
            PidsLimit = limits.PidsLimit,
        };

        return _api.PostAsync($"containers/{Reference(idOrName)}/update", request, cancellationToken);
    }

    /// <summary>
    /// Waits for a container to exit. No timeout is applied — cancel through
    /// <paramref name="cancellationToken"/> instead.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The container's exit code.</returns>
    public async Task<long> WaitForExitAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var response = await _api
            .PostAsync<ContainerWaitResponse>($"containers/{Reference(idOrName)}/wait", body: null,
                cancellationToken, applyTimeout: false)
            .ConfigureAwait(false);

        if (response.Error?.Message is { Length: > 0 } message)
        {
            throw new DockerException($"Waiting for container '{idOrName}' failed: {message}");
        }

        return response.StatusCode;
    }

    /// <summary>
    /// Prunes stopped containers.
    /// </summary>
    /// <param name="labelFilters">Optional label filters restricting what is pruned.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon has finished pruning.</returns>
    public Task PruneAsync(IDictionary<string, string> labelFilters = null,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder().AddLabelFilters(labelFilters);
        return _api.PostAsync<ContainersPruneResponse>(query.AppendTo("containers/prune"), body: null,
            cancellationToken, applyTimeout: false);
    }

    // ---------------------------------------------------------------------------------------
    // Statistics
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Takes a single resource-usage sample.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The sample. For a container that is not running the daemon returns empty <c>cpu_stats</c> and
    /// <c>memory_stats</c> objects, so check <see cref="ContainerStats.HasLiveData"/> before drawing
    /// conclusions.
    /// </returns>
    /// <remarks>
    /// The daemon takes two readings a second apart so that <see cref="ContainerStats.CpuPercent"/>
    /// has a baseline; expect this call to take about a second for a running container.
    /// </remarks>
    public Task<ContainerStats> GetStatsAsync(string idOrName, CancellationToken cancellationToken = default) =>
        _api.GetAsync<ContainerStats>($"containers/{Reference(idOrName)}/stats?stream=false", cancellationToken);

    /// <summary>
    /// Streams resource-usage samples roughly once per second until cancelled or the container stops.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token that ends the stream.</param>
    /// <returns>An asynchronous sequence of samples.</returns>
    public IAsyncEnumerable<ContainerStats> StreamStatsAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        _api.GetJsonLinesAsync<ContainerStats>($"containers/{Reference(idOrName)}/stats?stream=true",
            cancellationToken);

    // ---------------------------------------------------------------------------------------
    // Logs and exec
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a container's captured output, demultiplexed into the two streams.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="tail">The number of trailing lines to return, or <see langword="null"/> for all of them.</param>
    /// <param name="timestamps">When <see langword="true"/>, prefixes each line with its timestamp.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The captured output.</returns>
    public async Task<ContainerLogs> GetLogsAsync(string idOrName, int? tail = null, bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryStringBuilder()
            .Add("stdout", true)
            .Add("stderr", true)
            .AddIfTrue("timestamps", timestamps);

        if (tail.HasValue)
        {
            query.Add("tail", tail.Value.ToString(CultureInfo.InvariantCulture));
        }

        var path = query.AppendTo($"containers/{Reference(idOrName)}/logs");
        await using var stream = await _api.GetStreamAsync(path, cancellationToken).ConfigureAwait(false);
        return await MultiplexedStreamReader.ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a command inside a running container and captures its output and exit code.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="command">The command and its arguments, for example <c>["cat", "/proc/meminfo"]</c>.</param>
    /// <param name="user">The user to run as, or <see langword="null"/> for the container's default.</param>
    /// <param name="workingDir">The working directory, or <see langword="null"/> for the container's default.</param>
    /// <param name="env">Extra environment variables, each in <c>KEY=VALUE</c> form.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The command's output and exit code.</returns>
    /// <remarks>No standard input and no TTY are attached in this version.</remarks>
    public async Task<ExecResult> ExecAsync(string idOrName, IReadOnlyList<string> command, string user = null,
        string workingDir = null, IReadOnlyList<string> env = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
        {
            throw new ArgumentException("The command must contain at least one element.", nameof(command));
        }

        var createRequest = new ExecCreateRequest
        {
            AttachStdout = true,
            AttachStderr = true,
            Tty = false,
            Cmd = command,
            Env = env,
            User = user,
            WorkingDir = workingDir,
        };

        var created = await _api
            .PostAsync<ExecCreateResponse>($"containers/{Reference(idOrName)}/exec", createRequest, cancellationToken)
            .ConfigureAwait(false);

        var execId = created.Id
                     ?? throw new DockerException("The Docker daemon did not return an exec id.");

        ContainerLogs output;
        await using (var stream = await _api
                         .PostForStreamAsync($"exec/{execId}/start", new ExecStartRequest { Detach = false, Tty = false },
                             cancellationToken).ConfigureAwait(false))
        {
            output = await MultiplexedStreamReader.ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        var inspect = await _api
            .GetAsync<ExecInspectResponse>($"exec/{execId}/json", cancellationToken)
            .ConfigureAwait(false);

        return new ExecResult(output.Stdout, output.Stderr, inspect.ExitCode ?? 0);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static string Reference(string idOrName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrName);
        return Uri.EscapeDataString(idOrName.TrimStart('/'));
    }

    private static ContainerCreateRequest BuildCreateRequest(ContainerSpec spec)
    {
        var exposedPorts = new Dictionary<string, JsonEmptyObject>(StringComparer.Ordinal);
        var portBindings = new Dictionary<string, List<HostPortBinding>>(StringComparer.Ordinal);

        foreach (var port in spec.ExposedPorts)
        {
            exposedPorts[port.PortKey] = JsonEmptyObject.Instance;
        }

        foreach (var port in spec.PortBindings)
        {
            exposedPorts[port.PortKey] = JsonEmptyObject.Instance;
            portBindings[port.PortKey] =
            [
                new HostPortBinding
                {
                    HostPort = port.HostPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                },
            ];
        }

        var mounts = spec.Mounts.Count == 0
            ? null
            : spec.Mounts.Select(mount => new MountWire
            {
                Type = mount.TypeName,
                Source = mount.Source,
                Target = mount.Target,
                ReadOnly = mount.ReadOnly,
                TmpfsOptions = mount.Kind == MountKind.Tmpfs && mount.TmpfsSizeBytes.HasValue
                    ? new TmpfsOptionsWire { SizeBytes = mount.TmpfsSizeBytes }
                    : null,
            }).ToList();

        var limits = spec.Limits;

        LogConfig logConfig = null;
        if (!string.IsNullOrWhiteSpace(spec.LogDriver) || spec.LogOptions.Count > 0)
        {
            logConfig = new LogConfig
            {
                Type = string.IsNullOrWhiteSpace(spec.LogDriver) ? "json-file" : spec.LogDriver,
                Config = spec.LogOptions.Count > 0
                    ? new Dictionary<string, string>(spec.LogOptions, StringComparer.Ordinal)
                    : null,
            };
        }

        HostRestartPolicy restartPolicy = spec.RestartPolicy is null
            ? null
            : new HostRestartPolicy
            {
                Name = spec.RestartPolicy.Name,
                MaximumRetryCount = spec.RestartPolicy.Kind == RestartPolicyKind.OnFailure
                    ? spec.RestartPolicy.MaxRetries
                    : 0,
            };

        ContainerNetworkingConfig networkingConfig = null;
        if (!string.IsNullOrWhiteSpace(spec.NetworkName))
        {
            networkingConfig = new ContainerNetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointConfigWire>(StringComparer.Ordinal)
                {
                    [spec.NetworkName] = new EndpointConfigWire
                    {
                        Aliases = spec.NetworkAliases.Count > 0 ? spec.NetworkAliases.ToList() : null,
                    },
                },
            };
        }

        return new ContainerCreateRequest
        {
            Image = spec.Image,
            Cmd = spec.Command?.Count > 0 ? spec.Command : null,
            Entrypoint = spec.Entrypoint?.Count > 0 ? spec.Entrypoint : null,
            Env = spec.Env.Count > 0 ? spec.Env.ToList() : null,
            Labels = spec.Labels.Count > 0
                ? new Dictionary<string, string>(spec.Labels, StringComparer.Ordinal)
                : null,
            User = spec.User,
            WorkingDir = spec.WorkingDir,
            Hostname = spec.HostName,
            ExposedPorts = exposedPorts.Count > 0 ? exposedPorts : null,
            Healthcheck = spec.Healthcheck,
            NetworkingConfig = networkingConfig,
            HostConfig = new ContainerCreateHostConfig
            {
                PortBindings = portBindings.Count > 0 ? portBindings : null,
                Mounts = mounts,
                RestartPolicy = restartPolicy,
                AutoRemove = spec.AutoRemove ? true : null,
                Privileged = spec.Privileged ? true : null,
                LogConfig = logConfig,
                NanoCpus = limits?.ToNanoCpus(),
                CpusetCpus = limits?.CpusetCpus,
                CpuShares = limits?.CpuShares,
                Memory = limits?.MemoryBytes,
                MemoryReservation = limits?.MemoryReservationBytes,
                MemorySwap = limits?.MemorySwapBytes,
                PidsLimit = limits?.PidsLimit,
            },
        };
    }
}
