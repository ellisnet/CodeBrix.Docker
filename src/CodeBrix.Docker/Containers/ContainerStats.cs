using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A resource-usage sample for a container, from <c>GET /containers/{id}/stats</c>.
/// </summary>
/// <remarks>
/// The daemon answers for exited containers too, but with empty <c>cpu_stats</c> and
/// <c>memory_stats</c> objects. Every member is therefore nullable and every computed helper returns
/// <see langword="null"/> rather than a misleading zero when its inputs are missing.
/// </remarks>
public sealed class ContainerStats
{
    /// <summary>Gets the container id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    /// <summary>Gets the container name, including the daemon's leading slash.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; }

    /// <summary>Gets when this sample was taken.</summary>
    [JsonPropertyName("read")]
    public DateTimeOffset? Read { get; init; }

    /// <summary>Gets when the previous sample was taken, the baseline for the CPU delta.</summary>
    [JsonPropertyName("preread")]
    public DateTimeOffset? PreRead { get; init; }

    /// <summary>Gets the current CPU sample.</summary>
    [JsonPropertyName("cpu_stats")]
    public CpuStats CpuStats { get; init; }

    /// <summary>Gets the previous CPU sample, used to compute a percentage.</summary>
    [JsonPropertyName("precpu_stats")]
    public CpuStats PreCpuStats { get; init; }

    /// <summary>Gets the memory sample.</summary>
    [JsonPropertyName("memory_stats")]
    public MemoryStats MemoryStats { get; init; }

    /// <summary>Gets process and thread counts.</summary>
    [JsonPropertyName("pids_stats")]
    public PidsStats PidsStats { get; init; }

    /// <summary>Gets block-I/O counters.</summary>
    [JsonPropertyName("blkio_stats")]
    public BlkioStats BlkioStats { get; init; }

    /// <summary>Gets per-interface network counters, keyed by interface name.</summary>
    [JsonPropertyName("networks")]
    public IReadOnlyDictionary<string, NetworkStats> Networks { get; init; }

    /// <summary>Gets the number of processes, on Windows daemons.</summary>
    [JsonPropertyName("num_procs")]
    public int? NumProcs { get; init; }

    /// <summary>
    /// Gets a value indicating whether the daemon returned real counters. This is
    /// <see langword="false"/> for containers that are not running.
    /// </summary>
    /// <remarks>
    /// For a stopped container the daemon still answers, but with an empty <c>memory_stats</c> object
    /// and a <c>cpu_stats</c> object whose counters are all zero, so a non-null field is not by itself
    /// evidence of a live sample.
    /// </remarks>
    [JsonIgnore]
    public bool HasLiveData =>
        (MemoryStats?.Usage ?? 0) > 0
        || (CpuStats?.SystemCpuUsage ?? 0) > 0
        || (CpuStats?.CpuUsage?.TotalUsage ?? 0) > 0;

    /// <summary>
    /// Computes CPU usage as a percentage of one host CPU multiplied by the number of online CPUs —
    /// the same figure <c>docker stats</c> shows, so 100% means one full core.
    /// </summary>
    /// <returns>
    /// The percentage, or <see langword="null"/> when the sample lacks the deltas needed to compute it
    /// (an exited container, or a one-shot sample with no baseline).
    /// </returns>
    public double? CpuPercent()
    {
        var totalUsage = CpuStats?.CpuUsage?.TotalUsage;
        var preTotalUsage = PreCpuStats?.CpuUsage?.TotalUsage;
        var systemUsage = CpuStats?.SystemCpuUsage;
        var preSystemUsage = PreCpuStats?.SystemCpuUsage;

        if (totalUsage is null || preTotalUsage is null || systemUsage is null || preSystemUsage is null)
        {
            return null;
        }

        var cpuDelta = totalUsage.Value - preTotalUsage.Value;
        var systemDelta = systemUsage.Value - preSystemUsage.Value;
        if (cpuDelta < 0 || systemDelta <= 0)
        {
            return null;
        }

        var onlineCpus = CpuStats?.OnlineCpus
                         ?? CpuStats?.CpuUsage?.PerCpuUsage?.Count
                         ?? 1;
        if (onlineCpus <= 0)
        {
            onlineCpus = 1;
        }

        return (double)cpuDelta / systemDelta * onlineCpus * 100d;
    }

    /// <summary>
    /// Computes memory usage as a percentage of the container's limit, using the daemon's total usage
    /// figure (which includes reclaimable page cache).
    /// </summary>
    /// <returns>The percentage, or <see langword="null"/> when usage or limit is missing.</returns>
    public double? MemoryPercent()
    {
        var usage = MemoryStats?.Usage;
        var limit = MemoryStats?.Limit;
        if (usage is null || limit is null || limit.Value <= 0)
        {
            return null;
        }

        return (double)usage.Value / limit.Value * 100d;
    }

    /// <summary>
    /// Computes application (anonymous) memory as a percentage of the limit. This is the figure that
    /// predicts OOM kills, because page cache is reclaimed before the kernel kills anything.
    /// </summary>
    /// <returns>The percentage, or <see langword="null"/> when the breakdown or limit is missing.</returns>
    public double? EffectiveMemoryPercent()
    {
        var anon = MemoryStats?.AnonBytes;
        var limit = MemoryStats?.Limit;
        if (anon is null || limit is null || limit.Value <= 0)
        {
            return null;
        }

        return (double)anon.Value / limit.Value * 100d;
    }

    /// <summary>
    /// Gets the fraction of CFS scheduling periods in which the container was throttled, between 0 and 1.
    /// </summary>
    /// <returns>The throttle ratio, or <see langword="null"/> when no throttling counters are present.</returns>
    public double? ThrottleRatio() => CpuStats?.ThrottlingData?.ThrottleRatio();
}
