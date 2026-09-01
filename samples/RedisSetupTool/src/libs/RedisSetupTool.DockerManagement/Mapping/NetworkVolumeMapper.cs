using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns CodeBrix.Docker network and volume types into this library's DTOs.</summary>
internal static class NetworkVolumeMapper
{
    internal static NetworkInfo ToInfo(NetworkSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var labels = summary.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var config = FirstConfig(summary.Ipam);

        return new NetworkInfo
        {
            Id = summary.Id,
            ShortId = summary.ShortId,
            Name = summary.Name,
            Driver = summary.Driver,
            Scope = summary.Scope,
            Created = summary.Created,
            IsInternal = summary.Internal,
            IsAttachable = summary.Attachable,
            IsIngress = summary.Ingress,
            IsPredefined = summary.IsPredefined,
            Labels = labels,
            Subnet = config?.Subnet,
            Gateway = config?.Gateway,
            InstanceId = InstanceLabels.Read(labels, InstanceLabels.Instance),
        };
    }

    internal static NetworkInfo ToInfo(NetworkInspectResult inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);

        var labels = inspect.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var config = FirstConfig(inspect.Ipam);

        var attachments = new List<NetworkAttachmentInfo>();
        foreach (var pair in inspect.Containers
                 ?? new Dictionary<string, NetworkContainerAttachment>(StringComparer.Ordinal))
        {
            attachments.Add(new NetworkAttachmentInfo
            {
                ContainerName = pair.Value?.Name,
                EndpointId = pair.Value?.EndpointId,
                IPv4Address = pair.Value?.IPv4Address,
                MacAddress = pair.Value?.MacAddress,
            });
        }

        return new NetworkInfo
        {
            Id = inspect.Id,
            ShortId = inspect.ShortId,
            Name = inspect.Name,
            Driver = inspect.Driver,
            Scope = inspect.Scope,
            Created = inspect.Created,
            IsInternal = inspect.Internal,
            IsAttachable = inspect.Attachable,
            IsIngress = inspect.Ingress,
            Labels = labels,
            Subnet = config?.Subnet,
            Gateway = config?.Gateway,
            AttachedContainerCount = inspect.AttachedContainerCount,
            AttachedContainers = attachments,
            InstanceId = InstanceLabels.Read(labels, InstanceLabels.Instance),
        };
    }

    internal static VolumeInfo ToInfo(VolumeSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var labels = summary.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new VolumeInfo
        {
            Name = summary.Name,
            Driver = summary.Driver,
            Mountpoint = summary.Mountpoint,
            CreatedAt = summary.CreatedAt,
            Labels = labels,
            Scope = summary.Scope,
            InstanceId = InstanceLabels.Read(labels, InstanceLabels.Instance),
        };
    }

    internal static VolumeInfo ToInfo(VolumeInspectResult inspect)
    {
        ArgumentNullException.ThrowIfNull(inspect);

        var labels = inspect.Labels ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return new VolumeInfo
        {
            Name = inspect.Name,
            Driver = inspect.Driver,
            Mountpoint = inspect.Mountpoint,
            CreatedAt = inspect.CreatedAt,
            Labels = labels,
            Scope = inspect.Scope,
            SizeBytes = inspect.UsageData?.Size,
            RefCount = inspect.UsageData?.RefCount,
            InstanceId = InstanceLabels.Read(labels, InstanceLabels.Instance),
        };
    }

    private static NetworkIpamConfig FirstConfig(NetworkIpam ipam)
    {
        var configs = ipam?.Config;
        return configs is { Count: > 0 } ? configs[0] : null;
    }
}
