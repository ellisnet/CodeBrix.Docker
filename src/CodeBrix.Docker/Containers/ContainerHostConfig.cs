using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The host-side configuration of a container — resource limits, isolation, logging and networking —
/// from <c>HostConfig</c> in the inspect payload.
/// </summary>
public sealed class ContainerHostConfig
{
    /// <summary>
    /// Gets the CPU allowance in nano-CPUs; <c>250000000</c> is a quarter of a core. Zero means no
    /// hard CPU limit.
    /// </summary>
    [JsonPropertyName("NanoCpus")]
    public long NanoCpus { get; init; }

    /// <summary>Gets the CPUs the container is pinned to, for example <c>0-3</c>.</summary>
    [JsonPropertyName("CpusetCpus")]
    public string CpusetCpus { get; init; }

    /// <summary>Gets the relative CPU weight. Zero or <c>1024</c> means the default share.</summary>
    [JsonPropertyName("CpuShares")]
    public long CpuShares { get; init; }

    /// <summary>Gets the hard memory limit in bytes. Zero means unlimited.</summary>
    [JsonPropertyName("Memory")]
    public long Memory { get; init; }

    /// <summary>Gets the soft memory limit in bytes. Zero means none.</summary>
    [JsonPropertyName("MemoryReservation")]
    public long MemoryReservation { get; init; }

    /// <summary>
    /// Gets the combined memory + swap limit in bytes. Equal to <see cref="Memory"/> means swap is
    /// disabled; <c>-1</c> means unlimited swap.
    /// </summary>
    [JsonPropertyName("MemorySwap")]
    public long MemorySwap { get; init; }

    /// <summary>Gets the process/thread cap, or <see langword="null"/> when none is set.</summary>
    [JsonPropertyName("PidsLimit")]
    public long? PidsLimit { get; init; }

    /// <summary>Gets a value indicating whether the container runs privileged.</summary>
    [JsonPropertyName("Privileged")]
    public bool Privileged { get; init; }

    /// <summary>Gets a value indicating whether the daemon removes the container when it exits.</summary>
    [JsonPropertyName("AutoRemove")]
    public bool AutoRemove { get; init; }

    /// <summary>Gets a value indicating whether the root filesystem is read-only.</summary>
    [JsonPropertyName("ReadonlyRootfs")]
    public bool ReadonlyRootfs { get; init; }

    /// <summary>Gets the restart policy.</summary>
    [JsonPropertyName("RestartPolicy")]
    public HostRestartPolicy RestartPolicy { get; init; }

    /// <summary>Gets the logging driver configuration.</summary>
    [JsonPropertyName("LogConfig")]
    public LogConfig LogConfig { get; init; }

    /// <summary>Gets the network mode, for example <c>bridge</c>, <c>host</c> or a network name.</summary>
    [JsonPropertyName("NetworkMode")]
    public string NetworkMode { get; init; }

    /// <summary>Gets the legacy bind mounts, each in <c>source:target[:options]</c> form.</summary>
    [JsonPropertyName("Binds")]
    public IReadOnlyList<string> Binds { get; init; }

    /// <summary>Gets the tmpfs mounts, keyed by container path.</summary>
    [JsonPropertyName("Tmpfs")]
    public IReadOnlyDictionary<string, string> Tmpfs { get; init; }

    /// <summary>Gets a value indicating whether a hard CPU limit is in effect.</summary>
    [JsonIgnore]
    public bool HasCpuLimit => NanoCpus > 0;

    /// <summary>Gets a value indicating whether a hard memory limit is in effect.</summary>
    [JsonIgnore]
    public bool HasMemoryLimit => Memory > 0;

    /// <summary>
    /// Gets a value indicating whether swap is disabled — that is, a memory limit is set and the
    /// memory + swap limit equals it.
    /// </summary>
    [JsonIgnore]
    public bool IsSwapDisabled => Memory > 0 && MemorySwap == Memory;

    /// <summary>Gets <see cref="NanoCpus"/> expressed in whole cores, or <see langword="null"/> when unlimited.</summary>
    [JsonIgnore]
    public double? Cpus => NanoCpus > 0 ? NanoCpus / 1_000_000_000d : null;
}
