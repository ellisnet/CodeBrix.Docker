using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Instances;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>
/// Everything the builders share: the resolved parameters, the port plan, the network and its
/// gateway, resource naming, container creation, readiness polling and progress reporting.
/// </summary>
internal sealed class TopologyBuildContext
{
    private readonly IProgress<TopologyProgress> _progress;
    private readonly List<string> _volumes = [];
    private int _step;

    internal TopologyBuildContext(DockerClient client, TopologyDescriptor descriptor,
        TopologyRequest request, string instanceId, string instanceName, PortPlan ports,
        IReadOnlyDictionary<string, string> parameters, IProgress<TopologyProgress> progress,
        int totalSteps)
    {
        Client = client;
        Descriptor = descriptor;
        Request = request;
        InstanceId = instanceId;
        InstanceName = instanceName;
        Ports = ports;
        Parameters = parameters;
        CreatedAt = DateTimeOffset.UtcNow;
        NetworkName = InstanceId2Network(instanceId);
        TotalSteps = totalSteps;
        _progress = progress;
    }

    internal DockerClient Client { get; }

    internal TopologyDescriptor Descriptor { get; }

    internal TopologyRequest Request { get; }

    internal string InstanceId { get; }

    internal string InstanceName { get; }

    internal PortPlan Ports { get; }

    internal IReadOnlyDictionary<string, string> Parameters { get; }

    internal DateTimeOffset CreatedAt { get; }

    internal string NetworkName { get; }

    internal string Gateway { get; private set; }

    internal int TotalSteps { get; }

    internal IReadOnlyList<string> VolumeNames => _volumes;

    /// <summary>Gets the shared password, or an empty string when the topology has none.</summary>
    internal string Password => Parameter("password");

    internal string Parameter(string key) =>
        Parameters.TryGetValue(key, out var value) && value is not null ? value : string.Empty;

    internal int ParameterInt(string key, int fallback) =>
        int.TryParse(Parameter(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal void Report(string message) =>
        _progress?.Report(new TopologyProgress
        {
            Step = Interlocked.Increment(ref _step),
            TotalSteps = TotalSteps,
            Message = message,
        });

    /// <summary>Creates the instance's own bridge network and reads back its gateway address.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The gateway address the nodes announce.</returns>
    internal async Task<string> CreateNetworkAsync(CancellationToken cancellationToken)
    {
        Report($"Creating network {NetworkName}");
        var labels = BaseLabels("network");
        await Client.Networks.CreateAsync(NetworkName, "bridge", labels, cancellationToken)
            .ConfigureAwait(false);

        var inspect = await Client.Networks.InspectAsync(NetworkName, cancellationToken)
            .ConfigureAwait(false);
        var config = inspect.Ipam?.Config;
        Gateway = config is { Count: > 0 } ? config[0].Gateway : null;

        if (string.IsNullOrEmpty(Gateway))
        {
            throw new DockerManagementException(
                $"The network {NetworkName} came back without a gateway address, "
                + "which every announce-based topology needs.");
        }

        return Gateway;
    }

    /// <summary>Creates one node's volume.</summary>
    /// <param name="nodeIndex">The one-based node index.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volume name.</returns>
    internal async Task<string> CreateVolumeAsync(int nodeIndex, CancellationToken cancellationToken)
    {
        var name = InstanceId.Length == 0 ? null : Instances.InstanceId.VolumeName(InstanceId, nodeIndex);
        await Client.Volumes.CreateAsync(name, BaseLabels("volume"), cancellationToken)
            .ConfigureAwait(false);
        _volumes.Add(name);
        return name;
    }

    /// <summary>Creates and starts one node.</summary>
    /// <param name="node">What to create.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The node, with its container id filled in.</returns>
    internal async Task<TopologyNode> StartNodeAsync(NodePlan node, CancellationToken cancellationToken)
    {
        var containerName = Instances.InstanceId.ContainerName(InstanceId, node.RoleName);
        Report($"Starting {node.RoleName} on port {node.HostPort}");

        var volumeName = await CreateVolumeAsync(node.NodeIndex, cancellationToken)
            .ConfigureAwait(false);

        var labels = BaseLabels("container");
        labels[InstanceLabels.Role] = node.RoleLabel;
        labels[InstanceLabels.Node] = node.NodeIndex.ToString(CultureInfo.InvariantCulture);
        labels[InstanceLabels.Port] = node.HostPort.ToString(CultureInfo.InvariantCulture);
        if (node.BusHostPort.HasValue)
        {
            labels[InstanceLabels.BusPort] =
                node.BusHostPort.Value.ToString(CultureInfo.InvariantCulture);
        }

        var spec = new ContainerSpec
        {
            Image = Descriptor.Image,
            Name = containerName,
            Command = node.Command,
            Entrypoint = node.Entrypoint,
            Labels = labels,
            NetworkName = NetworkName,
            RestartPolicy = RestartPolicy.No,
            Limits = node.Limits,
        };

        spec.NetworkAliases.Add(node.RoleName);
        spec.PortBindings.Add(new PortBinding(node.ContainerPort, node.HostPort));
        if (node.BusHostPort.HasValue)
        {
            spec.PortBindings.Add(new PortBinding(node.BusHostPort.Value, node.BusHostPort.Value));
        }

        spec.Mounts.Add(MountSpec.Volume(volumeName, "/data"));

        var containerId = await Client.Containers.RunAsync(spec, cancellationToken)
            .ConfigureAwait(false);

        return new TopologyNode
        {
            ContainerId = containerId,
            ContainerName = containerName,
            Role = node.Role,
            NodeIndex = node.NodeIndex,
            ContainerPort = node.ContainerPort,
            HostPort = node.HostPort,
            BusHostPort = node.BusHostPort,
            VolumeName = volumeName,
            IsRunning = true,
            State = "running",
        };
    }

    /// <summary>Builds the label set every resource of the instance carries.</summary>
    /// <param name="resourceKind">One of <c>network</c>, <c>volume</c> or <c>container</c>.</param>
    /// <returns>A fresh dictionary.</returns>
    internal Dictionary<string, string> BaseLabels(string resourceKind)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [InstanceLabels.Instance] = InstanceId,
            [InstanceLabels.Topology] = Descriptor.Code,
            [InstanceLabels.Name] = InstanceName,
            [InstanceLabels.Created] = CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            [InstanceLabels.Image] = Descriptor.Image,
            [InstanceLabels.Resource] = resourceKind,
        };

        if (!string.IsNullOrEmpty(Gateway))
        {
            labels[InstanceLabels.AnnounceIp] = Gateway;
        }

        if (!string.IsNullOrEmpty(Password))
        {
            labels[InstanceLabels.Secret] = Password;
        }

        if (Parameters.TryGetValue("users", out var users) && !string.IsNullOrEmpty(users))
        {
            labels[InstanceLabels.Users] = users.Replace("\n", InstanceLabels.UserRecordSeparator,
                StringComparison.Ordinal);
        }

        if (Parameters.TryGetValue("serviceName", out var service) && !string.IsNullOrEmpty(service))
        {
            labels[InstanceLabels.Service] = service;
        }

        foreach (var extra in Request.ExtraLabels)
        {
            labels[extra.Key] = extra.Value;
        }

        return labels;
    }

    /// <summary>Builds a redis-cli argument list, adding the password when the topology has one.</summary>
    /// <param name="port">The port inside the container.</param>
    /// <param name="arguments">The command to send.</param>
    /// <returns>The full argument list.</returns>
    internal string[] RedisCli(int port, params string[] arguments)
    {
        var command = new List<string>
        {
            "redis-cli", "-p", port.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(Password))
        {
            command.Add("-a");
            command.Add(Password);
            command.Add("--no-auth-warning");
        }

        command.AddRange(arguments);
        return [.. command];
    }

    /// <summary>Runs one command inside a node and returns its combined output.</summary>
    /// <param name="containerName">The container to run in.</param>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The command's result.</returns>
    internal async Task<ExecResult> ExecAsync(string containerName, IReadOnlyList<string> command,
        CancellationToken cancellationToken) =>
        await Client.Containers.ExecAsync(containerName, command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Polls a condition until it holds or the timeout expires.</summary>
    /// <param name="what">What is being waited for, for the failure message.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="probe">The condition.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    internal async Task WaitAsync(string what, TimeSpan timeout, Func<Task<bool>> probe,
        CancellationToken cancellationToken)
    {
        Report($"Waiting for {what}");
        var clock = Stopwatch.StartNew();
        string lastFailure = null;

        while (clock.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await probe().ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception.Message;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new DockerManagementException(
            $"Timed out after {timeout.TotalSeconds:0} s waiting for {what}."
            + (lastFailure is null ? string.Empty : " Last failure: " + lastFailure));
    }

    /// <summary>Polls until a node answers PING.</summary>
    /// <param name="node">The node to poll.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the node answers.</returns>
    internal Task WaitForPongAsync(TopologyNode node, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        WaitAsync($"{node.ContainerName} to answer PING", timeout, async () =>
        {
            var result = await ExecAsync(node.ContainerName, RedisCli(node.ContainerPort, "ping"),
                cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                && result.Stdout.Contains("PONG", StringComparison.OrdinalIgnoreCase);
        }, cancellationToken);

    private static string InstanceId2Network(string instanceId) =>
        Instances.InstanceId.NetworkName(instanceId);
}
