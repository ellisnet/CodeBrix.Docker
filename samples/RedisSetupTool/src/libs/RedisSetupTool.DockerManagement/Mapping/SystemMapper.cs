using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns CodeBrix.Docker daemon-level types into this library's DTOs.</summary>
internal static class SystemMapper
{
    internal static DaemonInfo ToInfo(bool reachable, string endpoint, DockerVersionInfo version,
        DockerSystemInfo info) =>
        new()
        {
            IsReachable = reachable,
            Endpoint = endpoint,
            ServerVersion = version?.Version ?? info?.ServerVersion,
            ApiVersion = version?.ApiVersion,
            MinApiVersion = version?.MinApiVersion,
            OsType = info?.OsType ?? version?.Os,
            OperatingSystem = info?.OperatingSystem,
            KernelVersion = info?.KernelVersion ?? version?.KernelVersion,
            Architecture = info?.Architecture ?? version?.Arch,
            CgroupVersion = info?.CgroupVersion,
            CgroupDriver = info?.CgroupDriver,
            StorageDriver = info?.StorageDriver,
            LoggingDriver = info?.LoggingDriver,
            CpuCount = info?.NCpu ?? 0,
            TotalMemoryBytes = info?.MemTotal ?? 0,
            ContainerCount = info?.Containers ?? 0,
            ContainersRunning = info?.ContainersRunning ?? 0,
            ContainersPaused = info?.ContainersPaused ?? 0,
            ContainersStopped = info?.ContainersStopped ?? 0,
            ImageCount = info?.Images ?? 0,
            HasMemoryLimitSupport = info?.MemoryLimit == true,
            HasSwapLimitSupport = info?.SwapLimit == true,
            Warnings = info?.Warnings ?? [],
        };

    internal static DaemonDiskUsage ToUsage(DiskUsageInfo usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return new DaemonDiskUsage
        {
            LayersSizeBytes = usage.LayersSizeBytes,
            ImageCount = usage.ImageCount,
            ImagesSizeBytes = usage.ImagesSizeBytes,
            ReclaimableImageCount = usage.ReclaimableImageCount,
            ContainerCount = usage.ContainerCount,
            ContainersSizeBytes = usage.ContainersSizeBytes,
            VolumeCount = usage.VolumeCount,
            VolumesSizeBytes = usage.VolumesSizeBytes,
            ReclaimableVolumeCount = usage.ReclaimableVolumeCount,
            BuildCacheSizeBytes = usage.BuildCacheSizeBytes,
            ReclaimableBuildCacheBytes = usage.ReclaimableBuildCacheBytes,
            TotalSizeBytes = usage.TotalSizeBytes,
        };
    }

    internal static DaemonEvent ToEvent(DockerEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var type = source.Type ?? string.Empty;
        var action = source.Action ?? source.Status ?? string.Empty;
        var name = ReadAttribute(source, "name");
        var id = source.Actor?.Id ?? source.Id;

        return new DaemonEvent
        {
            Timestamp = source.Timestamp,
            Type = type,
            Action = action,
            ActorId = id,
            ActorName = name,
            Scope = source.Scope,
            Line = string.Join(' ', type, action, name ?? ContainerMapper.Shorten(id) ?? string.Empty)
                .Trim(),
        };
    }

    private static string ReadAttribute(DockerEvent source, string key)
    {
        var attributes = source.Actor?.Attributes;
        return attributes is not null && attributes.TryGetValue(key, out var value) ? value : null;
    }
}
