namespace CodeBrix.Docker;

/// <summary>
/// Typed cgroup resource limits for a container. Every property is optional; only the ones you set
/// are sent to the daemon.
/// </summary>
/// <remarks>
/// The same type is used at creation time (<see cref="ContainerSpec.Limits"/>) and for live retuning
/// through <see cref="ContainerOperations.UpdateResourcesAsync"/>.
/// </remarks>
public sealed class ResourceLimits
{
    /// <summary>
    /// Gets or sets the CPU allowance in whole cores, for example <c>0.5</c> for half a core.
    /// Maps to the daemon's <c>NanoCpus</c> (<c>Cpus * 1_000_000_000</c>), a hard CFS quota.
    /// </summary>
    public double? Cpus { get; set; }

    /// <summary>
    /// Gets or sets the CPUs the container may run on, for example <c>"0"</c>, <c>"0,1"</c> or <c>"0-3"</c>.
    /// This pins the container rather than limiting its share.
    /// </summary>
    public string CpusetCpus { get; set; }

    /// <summary>
    /// Gets or sets the relative CPU weight used when CPUs are contended (default <c>1024</c>).
    /// Unlike <see cref="Cpus"/> this is a soft priority, not a cap.
    /// </summary>
    public long? CpuShares { get; set; }

    /// <summary>Gets or sets the hard memory limit in bytes. Exceeding it triggers the kernel OOM killer.</summary>
    public long? MemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the soft memory limit in bytes. The kernel tries to keep the container below this
    /// value under host memory pressure. A reservation of roughly 70–80% of the limit is a good default.
    /// </summary>
    public long? MemoryReservationBytes { get; set; }

    /// <summary>
    /// Gets or sets the combined memory + swap limit in bytes. Set it equal to
    /// <see cref="MemoryBytes"/> to disable swap entirely, which makes memory behaviour predictable.
    /// </summary>
    public long? MemorySwapBytes { get; set; }

    /// <summary>Gets or sets the maximum number of processes/threads, guarding against fork bombs.</summary>
    public long? PidsLimit { get; set; }

    /// <summary>
    /// Converts a megabyte count to bytes.
    /// </summary>
    /// <param name="mb">The number of megabytes.</param>
    /// <returns>The equivalent number of bytes.</returns>
    public static long Megabytes(int mb) => mb * 1024L * 1024L;

    /// <summary>
    /// Converts a gigabyte count to bytes.
    /// </summary>
    /// <param name="gb">The number of gigabytes.</param>
    /// <returns>The equivalent number of bytes.</returns>
    public static long Gigabytes(int gb) => gb * 1024L * 1024L * 1024L;

    /// <summary>
    /// Gets <see cref="Cpus"/> expressed in the daemon's nano-CPU units, or <see langword="null"/>
    /// when no CPU allowance is set.
    /// </summary>
    /// <returns>The nano-CPU value.</returns>
    public long? ToNanoCpus() => Cpus.HasValue ? (long)(Cpus.Value * 1_000_000_000d) : null;

    /// <summary>
    /// Gets a value indicating whether any limit has been set.
    /// </summary>
    public bool IsEmpty =>
        Cpus is null && CpusetCpus is null && CpuShares is null && MemoryBytes is null
        && MemoryReservationBytes is null && MemorySwapBytes is null && PidsLimit is null;
}
