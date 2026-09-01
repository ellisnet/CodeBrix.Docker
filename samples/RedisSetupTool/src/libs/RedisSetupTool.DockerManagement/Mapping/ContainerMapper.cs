using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns CodeBrix.Docker container types into this library's DTOs.</summary>
internal static class ContainerMapper
{
    internal static ContainerInfo ToInfo(ContainerSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var labels = summary.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var mapped = new List<PortMapping>();
        foreach (var port in summary.Ports ?? [])
        {
            mapped.Add(ToPortMapping(port));
        }

        var ports = DeduplicatePorts(mapped);
        var managed = ReadManagedLabels(labels);

        return new ContainerInfo
        {
            Id = summary.Id,
            ShortId = Shorten(summary.Id),
            Name = summary.DisplayName,
            Image = summary.Image,
            ImageId = summary.ImageId,
            Command = summary.Command,
            Created = summary.Created,
            State = summary.State,
            Status = summary.Status,
            IsRunning = summary.IsRunning,
            Labels = labels,
            Ports = ports,
            SizeRwBytes = summary.SizeRw,
            SizeRootFsBytes = summary.SizeRootFs,
            InstanceId = managed.InstanceId,
            TopologyCode = managed.TopologyCode,
            Role = managed.Role,
            NodeIndex = managed.NodeIndex,
            IsManaged = managed.IsManaged,
        };
    }

    /// <summary>The four things this tool's labels say about a container.</summary>
    /// <param name="InstanceId">The instance id, or null.</param>
    /// <param name="TopologyCode">The topology code, or null.</param>
    /// <param name="Role">The node role, or null.</param>
    /// <param name="NodeIndex">The one-based node index, or null.</param>
    /// <param name="IsManaged">Whether this tool created the container.</param>
    internal readonly record struct ManagedLabels(string InstanceId, string TopologyCode, string Role,
        int? NodeIndex, bool IsManaged);

    internal static ManagedLabels ReadManagedLabels(IReadOnlyDictionary<string, string> labels)
    {
        var instanceId = InstanceLabels.Read(labels, InstanceLabels.Instance);
        var nodeText = InstanceLabels.Read(labels, InstanceLabels.Node);

        return new ManagedLabels(
            instanceId,
            InstanceLabels.Read(labels, InstanceLabels.Topology),
            InstanceLabels.Read(labels, InstanceLabels.Role),
            int.TryParse(nodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var node)
                ? node
                : null,
            !string.IsNullOrEmpty(instanceId));
    }

    internal static string FormatPortDisplay(int containerPort, int? hostPort, string protocol) =>
        hostPort.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0}/{1} -> {2}", containerPort, protocol,
                hostPort.Value)
            : string.Format(CultureInfo.InvariantCulture, "{0}/{1}", containerPort, protocol);

    /// <summary>
    /// Removes the duplicate a dual-stack publish produces. The daemon lists one entry per host
    /// binding, so a container published with <c>-p 6400:6379</c> comes back twice — once for
    /// <c>0.0.0.0</c> and once for <c>::</c> — and both render the same
    /// <c>6379/tcp -&gt; 6400</c>. Nothing in this tool cares which address family answered, so
    /// only the first of each (container port, host port, protocol) triple is kept, preferring
    /// the IPv4 binding when both are present.
    /// </summary>
    /// <param name="ports">The mapped ports, in the order the daemon listed them.</param>
    /// <returns>The ports with dual-stack duplicates collapsed.</returns>
    internal static IReadOnlyList<PortMapping> DeduplicatePorts(IReadOnlyList<PortMapping> ports)
    {
        if (ports is null || ports.Count < 2)
        {
            return ports ?? [];
        }

        var kept = new List<PortMapping>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            var key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                port.ContainerPort,
                port.HostPort.HasValue
                    ? port.HostPort.Value.ToString(CultureInfo.InvariantCulture)
                    : "-",
                port.Protocol);

            if (!seen.TryGetValue(key, out var index))
            {
                seen[key] = kept.Count;
                kept.Add(port);
                continue;
            }

            //Both address families published the same port: keep whichever entry names an
            //  IPv4 host address, because that is the one a client on this machine will use.
            if (IsIpv6(kept[index].HostIp) && !IsIpv6(port.HostIp))
            {
                kept[index] = port;
            }
        }

        return kept;
    }

    private static bool IsIpv6(string hostIp) =>
        !string.IsNullOrEmpty(hostIp) && hostIp.Contains(':');

    internal static PortMapping ToPortMapping(ContainerPort port)
    {
        var protocol = string.IsNullOrEmpty(port.Protocol) ? "tcp" : port.Protocol;
        var display = FormatPortDisplay(port.PrivatePort, port.PublicPort, protocol);

        return new PortMapping
        {
            ContainerPort = port.PrivatePort,
            HostPort = port.PublicPort,
            Protocol = protocol,
            HostIp = port.Ip,
            Display = display,
        };
    }

    internal static ContainerDetail ToDetail(ContainerInspectResult inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);

        var state = inspect.State;
        var config = inspect.Config;
        var host = inspect.HostConfig;
        var health = state?.Health;

        var networks = new List<ContainerNetworkAttachment>();
        foreach (var pair in inspect.NetworkSettings?.Networks
                 ?? new Dictionary<string, ContainerEndpointSettings>(StringComparer.Ordinal))
        {
            networks.Add(new ContainerNetworkAttachment
            {
                NetworkName = pair.Key,
                NetworkId = pair.Value?.NetworkId,
                IpAddress = pair.Value?.IpAddress,
                Gateway = pair.Value?.Gateway,
                MacAddress = pair.Value?.MacAddress,
                Aliases = pair.Value?.Aliases ?? [],
            });
        }

        var mounts = new List<MountInfo>();
        foreach (var mount in inspect.Mounts ?? [])
        {
            mounts.Add(new MountInfo
            {
                Type = mount.Type,
                Name = mount.Name,
                Source = mount.Source,
                Destination = mount.Destination,
                ReadWrite = mount.ReadWrite,
            });
        }

        return new ContainerDetail
        {
            Id = inspect.Id,
            ShortId = Shorten(inspect.Id),
            Name = inspect.DisplayName,
            Image = config?.Image ?? inspect.Image,
            Created = inspect.Created,
            StateStatus = state?.Status,
            IsRunning = state?.Running == true,
            IsPaused = state?.Paused == true,
            IsRestarting = state?.Restarting == true,
            WasOomKilled = state?.OomKilled == true,
            IsDead = state?.Dead == true,
            Pid = state?.Pid ?? 0,
            ExitCode = state?.ExitCode ?? 0,
            Error = state?.Error,
            StartedAt = state?.StartedAt,
            FinishedAt = state?.FinishedAt,
            RestartCount = inspect.RestartCount,
            HealthStatus = health?.Status,
            HealthFailingStreak = health?.FailingStreak ?? 0,
            IsHealthy = health?.IsHealthy == true,
            Command = config?.Cmd ?? [],
            Entrypoint = config?.Entrypoint ?? [],
            Env = config?.Env ?? [],
            Labels = config?.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal),
            WorkingDir = config?.WorkingDir,
            User = config?.User,
            Hostname = config?.Hostname,
            LogPath = inspect.LogPath,
            NetworkMode = host?.NetworkMode,
            Networks = networks,
            Mounts = mounts,
            Limits = ToLimits(host),
        };
    }

    internal static ResourceLimitInfo ToLimits(ContainerHostConfig host)
    {
        if (host is null)
        {
            return new ResourceLimitInfo();
        }

        return new ResourceLimitInfo
        {
            Cpus = host.Cpus,
            CpusetCpus = host.CpusetCpus,
            CpuShares = host.CpuShares,
            MemoryBytes = host.Memory,
            MemoryReservationBytes = host.MemoryReservation,
            MemorySwapBytes = host.MemorySwap,
            PidsLimit = host.PidsLimit,
            Privileged = host.Privileged,
            RestartPolicy = host.RestartPolicy?.Name,
            LogDriver = host.LogConfig?.Type,
            HasCpuLimit = host.HasCpuLimit,
            HasMemoryLimit = host.HasMemoryLimit,
            IsSwapDisabled = host.IsSwapDisabled,
        };
    }

    internal static ResourceLimits ToResourceLimits(ResourceLimitUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return new ResourceLimits
        {
            Cpus = update.Cpus,
            CpusetCpus = update.CpusetCpus,
            CpuShares = update.CpuShares,
            MemoryBytes = update.MemoryBytes,
            MemoryReservationBytes = update.MemoryReservationBytes,
            MemorySwapBytes = update.MemorySwapBytes,
            PidsLimit = update.PidsLimit,
        };
    }

    internal static ContainerLogText ToLogText(ContainerLogs logs) =>
        new()
        {
            Stdout = logs?.Stdout ?? string.Empty,
            Stderr = logs?.Stderr ?? string.Empty,
        };

    internal static CommandResult ToCommandResult(ExecResult result) =>
        new()
        {
            Stdout = result?.Stdout ?? string.Empty,
            Stderr = result?.Stderr ?? string.Empty,
            ExitCode = result?.ExitCode ?? -1,
        };

    internal static ContainerStatsSample ToStatsSample(ContainerStats stats, string containerId)
    {
        if (stats is null)
        {
            return new ContainerStatsSample { ContainerId = containerId, HasLiveData = false };
        }

        long rx = 0;
        long tx = 0;
        foreach (var network in stats.Networks ?? new Dictionary<string, NetworkStats>(StringComparer.Ordinal))
        {
            rx += network.Value?.RxBytes ?? 0;
            tx += network.Value?.TxBytes ?? 0;
        }

        return new ContainerStatsSample
        {
            ContainerId = string.IsNullOrEmpty(stats.Id) ? containerId : stats.Id,
            Name = stats.Name?.TrimStart('/'),
            Timestamp = stats.Read,
            HasLiveData = stats.HasLiveData,
            CpuPercent = stats.CpuPercent(),
            MemoryUsageBytes = stats.MemoryStats?.Usage,
            MemoryLimitBytes = stats.MemoryStats?.Limit,
            MemoryPercent = stats.MemoryPercent(),
            EffectiveMemoryPercent = stats.EffectiveMemoryPercent(),
            AnonBytes = stats.MemoryStats?.AnonBytes,
            FileBytes = stats.MemoryStats?.FileBytes,
            PidsCurrent = stats.PidsStats?.Current,
            PidsLimit = stats.PidsStats?.Limit,
            NetworkRxBytes = rx,
            NetworkTxBytes = tx,
            BlockReadBytes = stats.BlkioStats?.TotalBytes("read") ?? 0,
            BlockWriteBytes = stats.BlkioStats?.TotalBytes("write") ?? 0,
            ThrottleRatio = stats.ThrottleRatio(),
        };
    }

    internal static string Shorten(string id) =>
        string.IsNullOrEmpty(id) ? id : (id.Length >= 12 ? id[..12] : id);
}
