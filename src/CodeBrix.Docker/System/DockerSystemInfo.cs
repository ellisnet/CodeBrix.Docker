using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Daemon configuration and host details reported by <c>GET /info</c>.
/// </summary>
public sealed class DockerSystemInfo
{
    /// <summary>Gets the daemon's host name.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    /// <summary>Gets the daemon version.</summary>
    [JsonPropertyName("ServerVersion")]
    public string ServerVersion { get; init; }

    /// <summary>Gets the container platform, <c>linux</c> or <c>windows</c>.</summary>
    [JsonPropertyName("OSType")]
    public string OsType { get; init; }

    /// <summary>Gets the host operating system description.</summary>
    [JsonPropertyName("OperatingSystem")]
    public string OperatingSystem { get; init; }

    /// <summary>Gets the host kernel version.</summary>
    [JsonPropertyName("KernelVersion")]
    public string KernelVersion { get; init; }

    /// <summary>Gets the host CPU architecture.</summary>
    [JsonPropertyName("Architecture")]
    public string Architecture { get; init; }

    /// <summary>Gets the cgroup version in use, <c>1</c> or <c>2</c>. Resource semantics differ between them.</summary>
    [JsonPropertyName("CgroupVersion")]
    public string CgroupVersion { get; init; }

    /// <summary>Gets the cgroup driver, for example <c>systemd</c> or <c>cgroupfs</c>.</summary>
    [JsonPropertyName("CgroupDriver")]
    public string CgroupDriver { get; init; }

    /// <summary>Gets the storage driver, for example <c>overlay2</c>.</summary>
    [JsonPropertyName("Driver")]
    public string StorageDriver { get; init; }

    /// <summary>Gets the default logging driver, for example <c>json-file</c>.</summary>
    [JsonPropertyName("LoggingDriver")]
    public string LoggingDriver { get; init; }

    /// <summary>Gets the number of CPUs available to the daemon.</summary>
    [JsonPropertyName("NCPU")]
    public long NCpu { get; init; }

    /// <summary>Gets the total memory available to the daemon, in bytes.</summary>
    [JsonPropertyName("MemTotal")]
    public long MemTotal { get; init; }

    /// <summary>Gets the total number of containers known to the daemon.</summary>
    [JsonPropertyName("Containers")]
    public long Containers { get; init; }

    /// <summary>Gets the number of running containers.</summary>
    [JsonPropertyName("ContainersRunning")]
    public long ContainersRunning { get; init; }

    /// <summary>Gets the number of paused containers.</summary>
    [JsonPropertyName("ContainersPaused")]
    public long ContainersPaused { get; init; }

    /// <summary>Gets the number of stopped containers.</summary>
    [JsonPropertyName("ContainersStopped")]
    public long ContainersStopped { get; init; }

    /// <summary>Gets the number of images stored locally.</summary>
    [JsonPropertyName("Images")]
    public long Images { get; init; }

    /// <summary>Gets a value indicating whether the daemon supports memory limits.</summary>
    [JsonPropertyName("MemoryLimit")]
    public bool MemoryLimit { get; init; }

    /// <summary>Gets a value indicating whether the daemon supports swap limits.</summary>
    [JsonPropertyName("SwapLimit")]
    public bool SwapLimit { get; init; }

    /// <summary>Gets a value indicating whether the daemon supports CPU CFS quotas (hard CPU limits).</summary>
    [JsonPropertyName("CpuCfsQuota")]
    public bool CpuCfsQuota { get; init; }

    /// <summary>Gets a value indicating whether the daemon supports PID limits.</summary>
    [JsonPropertyName("PidsLimit")]
    public bool PidsLimit { get; init; }

    /// <summary>Gets warnings the daemon reports about the current host configuration.</summary>
    [JsonPropertyName("Warnings")]
    public IReadOnlyList<string> Warnings { get; init; }
}
