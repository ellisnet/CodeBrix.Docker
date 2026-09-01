using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Disk-usage totals reported by <c>GET /system/df</c>.
/// </summary>
public sealed class DiskUsageInfo
{
    /// <summary>Gets the total size of all image layers, in bytes.</summary>
    public long LayersSizeBytes { get; init; }

    /// <summary>Gets the number of images stored locally.</summary>
    public int ImageCount { get; init; }

    /// <summary>Gets the total size of all images, in bytes (shared layers counted once per image).</summary>
    public long ImagesSizeBytes { get; init; }

    /// <summary>Gets the number of images not referenced by any tag or container.</summary>
    public int ReclaimableImageCount { get; init; }

    /// <summary>Gets the number of containers, running or not.</summary>
    public int ContainerCount { get; init; }

    /// <summary>Gets the total writable-layer size of all containers, in bytes.</summary>
    public long ContainersSizeBytes { get; init; }

    /// <summary>Gets the number of volumes.</summary>
    public int VolumeCount { get; init; }

    /// <summary>Gets the total size of all volumes, in bytes. Negative daemon values are reported as zero.</summary>
    public long VolumesSizeBytes { get; init; }

    /// <summary>Gets the number of volumes not referenced by any container.</summary>
    public int ReclaimableVolumeCount { get; init; }

    /// <summary>Gets the total size of the build cache, in bytes.</summary>
    public long BuildCacheSizeBytes { get; init; }

    /// <summary>Gets the build-cache bytes the daemon reports as reclaimable.</summary>
    public long ReclaimableBuildCacheBytes { get; init; }

    /// <summary>Gets the sum of image, container, volume and build-cache usage, in bytes.</summary>
    public long TotalSizeBytes =>
        ImagesSizeBytes + ContainersSizeBytes + VolumesSizeBytes + BuildCacheSizeBytes;
}

/// <summary>Wire shape of <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageResponse
{
    [JsonPropertyName("LayersSize")]
    public long LayersSize { get; init; }

    [JsonPropertyName("Images")]
    public List<DiskUsageImage> Images { get; init; }

    [JsonPropertyName("Containers")]
    public List<DiskUsageContainer> Containers { get; init; }

    [JsonPropertyName("Volumes")]
    public List<DiskUsageVolume> Volumes { get; init; }

    [JsonPropertyName("BuildCache")]
    public List<DiskUsageBuildCacheRecord> BuildCache { get; init; }
}

/// <summary>Wire shape of an image entry in <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageImage
{
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    [JsonPropertyName("Containers")]
    public long Containers { get; init; }

    [JsonPropertyName("RepoTags")]
    public List<string> RepoTags { get; init; }
}

/// <summary>Wire shape of a container entry in <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageContainer
{
    [JsonPropertyName("SizeRw")]
    public long SizeRw { get; init; }
}

/// <summary>Wire shape of a volume entry in <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageVolume
{
    [JsonPropertyName("UsageData")]
    public DiskUsageVolumeUsage UsageData { get; init; }
}

/// <summary>Wire shape of a volume's usage data in <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageVolumeUsage
{
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    [JsonPropertyName("RefCount")]
    public long RefCount { get; init; }
}

/// <summary>Wire shape of a build-cache entry in <c>GET /system/df</c>.</summary>
internal sealed class DiskUsageBuildCacheRecord
{
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    [JsonPropertyName("InUse")]
    public bool InUse { get; init; }

    [JsonPropertyName("Shared")]
    public bool Shared { get; init; }
}
