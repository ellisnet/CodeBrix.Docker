using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.DockerManagement.Topologies.Builders;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>The one implementation of <see cref="IRedisTopologyService"/>.</summary>
public sealed class RedisTopologyService : IRedisTopologyService
{
    private readonly DockerManager _docker;
    private readonly IHostPortAllocator _ports;
    private readonly ITopologyBuilder[] _builders =
    [
        new StandaloneTopologyBuilder(),
        new ReplicaTopologyBuilder(),
        new SentinelTopologyBuilder(),
        new ClusterTopologyBuilder(),
        new QuorumTopologyBuilder(),
    ];

    /// <summary>Creates the service.</summary>
    /// <param name="docker">The Docker facade.</param>
    /// <param name="ports">The host port allocator.</param>
    public RedisTopologyService(DockerManager docker, IHostPortAllocator ports)
    {
        _docker = docker ?? throw new ArgumentNullException(nameof(docker));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    /// <inheritdoc />
    public IReadOnlyList<TopologyDescriptor> Catalog => TopologyCatalog.All;

    /// <inheritdoc />
    public TopologyDescriptor Describe(TopologyId id) => TopologyCatalog.Get(id);

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(TopologyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problems = new List<string>();
        var descriptor = TopologyCatalog.Get(request.TopologyId);

        foreach (var parameter in descriptor.Parameters)
        {
            var value = request.Parameters.TryGetValue(parameter.Key, out var supplied)
                ? supplied
                : parameter.DefaultValue;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (parameter.IsRequired)
                {
                    problems.Add($"{parameter.Label} is required.");
                }

                continue;
            }

            switch (parameter.Kind)
            {
                case TopologyParameterKind.Integer:
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var number))
                    {
                        problems.Add($"{parameter.Label} must be a whole number.");
                    }
                    else if (parameter.Minimum.HasValue && number < parameter.Minimum.Value)
                    {
                        problems.Add(
                            $"{parameter.Label} must be at least {parameter.Minimum.Value}.");
                    }
                    else if (parameter.Maximum.HasValue && number > parameter.Maximum.Value)
                    {
                        problems.Add($"{parameter.Label} must be at most {parameter.Maximum.Value}.");
                    }

                    break;

                case TopologyParameterKind.Choice:
                    if (!parameter.Choices.Contains(value))
                    {
                        problems.Add($"{parameter.Label} must be one of "
                            + string.Join(", ", parameter.Choices) + ".");
                    }

                    break;

                case TopologyParameterKind.MultiLineText when parameter.Key == "users":
                    var lines = value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0)
                    {
                        problems.Add($"{parameter.Label} needs at least one user.");
                    }

                    foreach (var line in lines)
                    {
                        if (!StandaloneTopologyBuilder.TryParseUser(line, out _))
                        {
                            problems.Add($"\"{line.Trim()}\" is not a "
                                + "name:password:permissions user line.");
                        }
                    }

                    break;

                default:
                    break;
            }
        }

        return problems;
    }

    /// <inheritdoc />
    public Task<PortPlan> PreviewPortsAsync(TopologyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _ports.PreviewAsync(TopologyCatalog.Get(request.TopologyId), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TopologyInstance> CreateAsync(TopologyRequest request,
        IProgress<TopologyProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problems = Validate(request);
        if (problems.Count > 0)
        {
            throw new DockerManagementException("The request is not valid: "
                + string.Join(" ", problems));
        }

        var descriptor = TopologyCatalog.Get(request.TopologyId);
        var builder = BuilderFor(request.TopologyId);
        var parameters = ResolveParameters(descriptor, request);
        var instanceId = InstanceId.Create(request.TopologyId);
        var instanceName = string.IsNullOrWhiteSpace(request.InstanceName)
            ? descriptor.Code.ToLowerInvariant() + "-" + instanceId[^8..]
            : request.InstanceName.Trim();

        var plan = await _ports.AllocateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var context = new TopologyBuildContext(_docker.Client, descriptor, request, instanceId,
            instanceName, plan, parameters, progress, builder.StepCount(descriptor));

        try
        {
            var built = await builder.BuildAsync(context, cancellationToken).ConfigureAwait(false);
            context.Report("Ready");

            return new TopologyInstance
            {
                InstanceId = instanceId,
                InstanceName = instanceName,
                TopologyId = descriptor.Id,
                TopologyCode = descriptor.Code,
                Image = descriptor.Image,
                CreatedAt = context.CreatedAt,
                State = InstanceState.Running,
                StatusText = Status(built.Nodes.Count, built.Nodes.Count),
                NetworkName = context.NetworkName,
                AnnounceIp = context.Gateway,
                VolumeNames = context.VolumeNames,
                Nodes = built.Nodes,
                Connection = built.Connection,
            };
        }
        catch (Exception exception)
        {
            progress?.Report(new TopologyProgress
            {
                Step = builder.StepCount(descriptor),
                TotalSteps = builder.StepCount(descriptor),
                Message = "Failed: " + exception.Message + " - rolling back",
                IsFailure = true,
            });

            try
            {
                await DestroyAsync(instanceId, progress: null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                //A rollback failure must not hide the original one.
            }

            throw;
        }
        finally
        {
            _ports.Release(plan);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TopologyInstance>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var containers = await _docker.ListManagedContainersAsync(cancellationToken)
            .ConfigureAwait(false);
        var volumes = await _docker.ListVolumesAsync(cancellationToken).ConfigureAwait(false);

        var grouped = new Dictionary<string, List<ContainerInfo>>(StringComparer.Ordinal);
        foreach (var container in containers)
        {
            if (string.IsNullOrEmpty(container.InstanceId))
            {
                continue;
            }

            if (!grouped.TryGetValue(container.InstanceId, out var list))
            {
                list = [];
                grouped[container.InstanceId] = list;
            }

            list.Add(container);
        }

        var instances = new List<TopologyInstance>(grouped.Count);
        foreach (var pair in grouped)
        {
            var instance = await RebuildAsync(pair.Key, pair.Value, volumes, cancellationToken)
                .ConfigureAwait(false);
            if (instance is not null)
            {
                instances.Add(instance);
            }
        }

        instances.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
        return instances;
    }

    /// <inheritdoc />
    public async Task<TopologyInstance> RefreshAsync(string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var containers = await _docker.ListInstanceContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (containers.Count == 0)
        {
            return null;
        }

        var volumes = await _docker.ListVolumesAsync(cancellationToken).ConfigureAwait(false);
        return await RebuildAsync(instanceId, [.. containers], volumes, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var containers = await OrderedContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var container in containers)
        {
            if (!container.IsRunning)
            {
                await _docker.StartContainerAsync(container.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        //nodes.conf on each volume restores a cluster, so the create step is deliberately not re-run.
        await WaitForFirstNodeAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        var containers = await OrderedContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        for (var index = containers.Count - 1; index >= 0; index--)
        {
            if (containers[index].IsRunning)
            {
                await _docker.StopContainerAsync(containers[index].Id, 10, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task RestartAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        await StopAsync(instanceId, cancellationToken).ConfigureAwait(false);
        await StartAsync(instanceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DestroyAsync(string instanceId, IProgress<TopologyProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var step = 0;
        void Report(string message) => progress?.Report(new TopologyProgress
        {
            Step = ++step,
            TotalSteps = 3,
            Message = message,
        });

        Report("Removing containers");
        var containers = await _docker.ListInstanceContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var container in containers)
        {
            await QuietlyAsync(() => _docker.RemoveContainerAsync(container.Id, force: true,
                removeVolumes: false, cancellationToken)).ConfigureAwait(false);
        }

        Report("Removing volumes");
        var volumes = await _docker.ListVolumesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var volume in volumes)
        {
            if (string.Equals(volume.InstanceId, instanceId, StringComparison.Ordinal))
            {
                await QuietlyAsync(() => _docker.RemoveVolumeAsync(volume.Name, force: true,
                    cancellationToken)).ConfigureAwait(false);
            }
        }

        Report("Removing the network");
        var networks = await _docker.ListNetworksAsync(cancellationToken).ConfigureAwait(false);
        foreach (var network in networks)
        {
            if (string.Equals(network.InstanceId, instanceId, StringComparison.Ordinal))
            {
                await QuietlyAsync(() => _docker.RemoveNetworkAsync(network.Id, cancellationToken))
                    .ConfigureAwait(false);
            }
        }
    }

    private static string Status(int running, int total) => string.Format(CultureInfo.InvariantCulture,
        "{0} of {1} running", running, total);

    private static async Task QuietlyAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (DockerManagementException)
        {
            //Teardown is idempotent; a resource that is already gone is not a failure.
        }
    }

    private ITopologyBuilder BuilderFor(TopologyId id)
    {
        foreach (var builder in _builders)
        {
            if (builder.Supported.Contains(id))
            {
                return builder;
            }
        }

        throw new DockerManagementException($"No builder knows how to create {id}.");
    }

    private static IReadOnlyDictionary<string, string> ResolveParameters(
        TopologyDescriptor descriptor, TopologyRequest request)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in descriptor.Parameters)
        {
            var value = request.Parameters.TryGetValue(parameter.Key, out var supplied)
                ? supplied
                : parameter.DefaultValue;

            value = ReplaceGenerated(value ?? string.Empty);
            resolved[parameter.Key] = value;
        }

        return resolved;
    }

    private static string ReplaceGenerated(string value)
    {
        while (value.Contains(TopologyParameter.GeneratedToken, StringComparison.Ordinal))
        {
            var index = value.IndexOf(TopologyParameter.GeneratedToken, StringComparison.Ordinal);
            value = value[..index] + GeneratePassword()
                + value[(index + TopologyParameter.GeneratedToken.Length)..];
        }

        return value;
    }

    private static string GeneratePassword() => "redis-" + InstanceId.RandomHex(12);

    private async Task<IReadOnlyList<ContainerInfo>> OrderedContainersAsync(string instanceId,
        CancellationToken cancellationToken)
    {
        var containers = await _docker.ListInstanceContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        var ordered = new List<ContainerInfo>(containers);
        ordered.Sort((left, right) => (left.NodeIndex ?? 0).CompareTo(right.NodeIndex ?? 0));
        return ordered;
    }

    private async Task WaitForFirstNodeAsync(string instanceId, CancellationToken cancellationToken)
    {
        var containers = await OrderedContainersAsync(instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (containers.Count == 0)
        {
            return;
        }

        var first = containers[0];
        var password = LabelOf(containers, InstanceLabels.Secret);
        var port = PortInsideContainer(first);
        var command = new List<string>
        {
            "redis-cli", "-p", port.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(password))
        {
            command.Add("-a");
            command.Add(password);
            command.Add("--no-auth-warning");
        }

        command.Add("ping");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var result = await _docker.RunCommandAsync(first.Id, command,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.Stdout.Contains("PONG", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (DockerManagementException)
            {
                //The container may still be coming up; keep polling until the deadline.
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int PortInsideContainer(ContainerInfo container)
    {
        foreach (var port in container.Ports)
        {
            if (port.HostPort.HasValue)
            {
                return port.ContainerPort;
            }
        }

        return 6379;
    }

    private static string LabelOf(IReadOnlyList<ContainerInfo> containers, string label)
    {
        foreach (var container in containers)
        {
            var value = InstanceLabels.Read(container.Labels, label);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private async Task<TopologyInstance> RebuildAsync(string instanceId,
        List<ContainerInfo> containers, IReadOnlyList<VolumeInfo> volumes,
        CancellationToken cancellationToken)
    {
        if (containers.Count == 0)
        {
            return null;
        }

        containers.Sort((left, right) => (left.NodeIndex ?? 0).CompareTo(right.NodeIndex ?? 0));

        var code = LabelOf(containers, InstanceLabels.Topology);
        if (!TopologyCatalog.TryParseCode(code, out var topologyId)
            && !InstanceId.TryParseTopology(instanceId, out topologyId))
        {
            return null;
        }

        var descriptor = TopologyCatalog.Get(topologyId);
        var name = LabelOf(containers, InstanceLabels.Name) ?? instanceId;
        var image = LabelOf(containers, InstanceLabels.Image) ?? descriptor.Image;
        var announceIp = LabelOf(containers, InstanceLabels.AnnounceIp);
        var password = LabelOf(containers, InstanceLabels.Secret);
        var service = LabelOf(containers, InstanceLabels.Service);
        var usersLabel = LabelOf(containers, InstanceLabels.Users);
        var createdText = LabelOf(containers, InstanceLabels.Created);
        var created = DateTimeOffset.TryParse(createdText, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.MinValue;

        var nodes = new List<TopologyNode>(containers.Count);
        var volumeNames = new List<string>();
        foreach (var volume in volumes)
        {
            if (string.Equals(volume.InstanceId, instanceId, StringComparison.Ordinal))
            {
                volumeNames.Add(volume.Name);
            }
        }

        var roles = await ResolveClusterRolesAsync(descriptor, containers, announceIp,
            cancellationToken).ConfigureAwait(false);

        foreach (var container in containers)
        {
            var index = container.NodeIndex ?? nodes.Count + 1;
            var role = roles is not null && roles.TryGetValue(index, out var live)
                ? live
                : RoleFromLabel(container.Role);

            nodes.Add(new TopologyNode
            {
                ContainerId = container.Id,
                ContainerName = container.Name,
                Role = role,
                NodeIndex = index,
                ContainerPort = PortInsideContainer(container),
                HostPort = ReadInt(InstanceLabels.Read(container.Labels, InstanceLabels.Port)) ?? 0,
                BusHostPort = ReadInt(InstanceLabels.Read(container.Labels, InstanceLabels.BusPort)),
                VolumeName = InstanceId.VolumeName(instanceId, index),
                IsRunning = container.IsRunning,
                State = container.State,
            });
        }

        var running = 0;
        foreach (var node in nodes)
        {
            if (node.IsRunning)
            {
                running++;
            }
        }

        var state = running == nodes.Count
            ? InstanceState.Running
            : running == 0 ? InstanceState.Stopped : InstanceState.Partial;

        return new TopologyInstance
        {
            InstanceId = instanceId,
            InstanceName = name,
            TopologyId = descriptor.Id,
            TopologyCode = descriptor.Code,
            Image = image,
            CreatedAt = created,
            State = state,
            StatusText = Status(running, nodes.Count),
            NetworkName = InstanceId.NetworkName(instanceId),
            AnnounceIp = announceIp,
            VolumeNames = volumeNames,
            Nodes = nodes,
            Connection = BuildConnection(descriptor, nodes, password, service, usersLabel, announceIp),
        };
    }

    private async Task<Dictionary<int, NodeRole>> ResolveClusterRolesAsync(
        TopologyDescriptor descriptor, IReadOnlyList<ContainerInfo> containers, string announceIp,
        CancellationToken cancellationToken)
    {
        if (descriptor.ConnectionShape != ConnectionShape.Cluster || string.IsNullOrEmpty(announceIp))
        {
            return null;
        }

        ContainerInfo runner = null;
        foreach (var container in containers)
        {
            if (container.IsRunning)
            {
                runner = container;
                break;
            }
        }

        if (runner is null)
        {
            return null;
        }

        try
        {
            var command = new List<string>
            {
                "redis-cli", "-p",
                PortInsideContainer(runner).ToString(CultureInfo.InvariantCulture),
            };

            var password = LabelOf(containers, InstanceLabels.Secret);
            if (!string.IsNullOrEmpty(password))
            {
                command.Add("-a");
                command.Add(password);
                command.Add("--no-auth-warning");
            }

            command.Add("cluster");
            command.Add("nodes");

            var result = await _docker.RunCommandAsync(runner.Id, command,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var roles = new Dictionary<int, NodeRole>();
            foreach (var container in containers)
            {
                var index = container.NodeIndex ?? 0;
                var port = ReadInt(InstanceLabels.Read(container.Labels, InstanceLabels.Port)) ?? 0;
                var line = ClusterTopologyBuilder.FindLine(result.Stdout, announceIp, port);
                roles[index] = line is not null && line.Contains("slave", StringComparison.Ordinal)
                    ? NodeRole.ClusterReplica
                    : NodeRole.ClusterPrimary;
            }

            return roles;
        }
        catch (DockerManagementException)
        {
            return null;
        }
    }

    private static ConnectionInfo BuildConnection(TopologyDescriptor descriptor,
        IReadOnlyList<TopologyNode> nodes, string password, string service, string usersLabel,
        string announceIp)
    {
        var endpoints = new List<RedisEndpoint>();
        var shape = descriptor.ConnectionShape;

        if (shape == ConnectionShape.Sentinel)
        {
            foreach (var node in nodes)
            {
                if (node.Role == NodeRole.Sentinel)
                {
                    endpoints.Add(new RedisEndpoint
                    {
                        Host = "127.0.0.1",
                        Port = node.HostPort,
                        Role = NodeRole.Sentinel,
                        NodeIndex = node.NodeIndex,
                        IsSentinel = true,
                    });
                }
            }

            foreach (var node in nodes)
            {
                if (node.Role != NodeRole.Sentinel)
                {
                    endpoints.Add(new RedisEndpoint
                    {
                        Host = "127.0.0.1",
                        Port = node.HostPort,
                        Role = node.Role,
                        NodeIndex = node.NodeIndex,
                    });
                }
            }
        }
        else
        {
            var host = shape == ConnectionShape.Cluster && !string.IsNullOrEmpty(announceIp)
                ? announceIp
                : "127.0.0.1";

            foreach (var node in nodes)
            {
                endpoints.Add(new RedisEndpoint
                {
                    Host = host,
                    Port = node.HostPort,
                    Role = node.Role,
                    NodeIndex = node.NodeIndex,
                });
            }
        }

        var users = StandaloneTopologyBuilder.ParseUsers(
            (usersLabel ?? string.Empty).Replace(InstanceLabels.UserRecordSeparator, "\n",
                StringComparison.Ordinal));

        var cli = descriptor.Id == TopologyId.E4
            ? ConnectionInfoFactory.ValkeyCli
            : ConnectionInfoFactory.RedisCli;

        return ConnectionInfoFactory.Build(shape, endpoints, password,
            string.IsNullOrEmpty(service) ? null : service, users, cliExecutable: cli);
    }

    private static NodeRole RoleFromLabel(string role) => role switch
    {
        "primary" => NodeRole.Primary,
        "replica" => NodeRole.Replica,
        "sentinel" => NodeRole.Sentinel,
        "cluster-primary" => NodeRole.ClusterPrimary,
        "cluster-replica" => NodeRole.ClusterReplica,
        "master" => NodeRole.QuorumMaster,
        _ => NodeRole.Primary,
    };

    private static int? ReadInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
