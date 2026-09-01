namespace RedisSetupTool.DockerManagement.Models;

/// <summary>Disk consumed by images, containers, volumes and the build cache.</summary>
public sealed class DaemonDiskUsage
{
    /// <summary>Gets the total size of all image layers, in bytes.</summary>
    public long LayersSizeBytes { get; init; }

    /// <summary>Gets the number of images.</summary>
    public int ImageCount { get; init; }

    /// <summary>Gets the size of all images, in bytes.</summary>
    public long ImagesSizeBytes { get; init; }

    /// <summary>Gets the number of images that could be reclaimed.</summary>
    public int ReclaimableImageCount { get; init; }

    /// <summary>Gets the number of containers.</summary>
    public int ContainerCount { get; init; }

    /// <summary>Gets the writable-layer size of all containers, in bytes.</summary>
    public long ContainersSizeBytes { get; init; }

    /// <summary>Gets the number of volumes.</summary>
    public int VolumeCount { get; init; }

    /// <summary>Gets the size of all volumes, in bytes.</summary>
    public long VolumesSizeBytes { get; init; }

    /// <summary>Gets the number of volumes that could be reclaimed.</summary>
    public int ReclaimableVolumeCount { get; init; }

    /// <summary>Gets the size of the build cache, in bytes.</summary>
    public long BuildCacheSizeBytes { get; init; }

    /// <summary>Gets the reclaimable part of the build cache, in bytes.</summary>
    public long ReclaimableBuildCacheBytes { get; init; }

    /// <summary>Gets the sum of images, containers, volumes and build cache, in bytes.</summary>
    public long TotalSizeBytes { get; init; }
}
