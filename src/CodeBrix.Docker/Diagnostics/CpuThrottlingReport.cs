using System;

namespace CodeBrix.Docker;

/// <summary>
/// What the kernel's CFS throttling counters say about a container's CPU quota — the evidence that a
/// <c>--cpus</c> limit is actually costing the workload time.
/// </summary>
public sealed class CpuThrottlingReport
{
    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the daemon returned live counters. This is
    /// <see langword="false"/> for a container that is not running, in which case every counter below
    /// is zero.
    /// </summary>
    public bool HasLiveData { get; init; }

    /// <summary>Gets the number of CFS scheduling periods the container has been through.</summary>
    public long Periods { get; init; }

    /// <summary>Gets how many of those periods ended with the container throttled.</summary>
    public long ThrottledPeriods { get; init; }

    /// <summary>Gets the total time the container spent throttled, in nanoseconds.</summary>
    public long ThrottledTimeNanos { get; init; }

    /// <summary>
    /// Gets the fraction of scheduling periods in which the container was throttled, between 0 and 1.
    /// This is 0 when no periods have been recorded, which is what a container with no CPU quota reports.
    /// </summary>
    public double ThrottleRatio { get; init; }

    /// <summary>Gets the severity band <see cref="ThrottleRatio"/> falls into.</summary>
    public ThrottleSeverity Severity { get; init; }

    /// <summary>Gets a one-sentence, human-readable reading of the counters above.</summary>
    public string Interpretation { get; init; } = string.Empty;

    /// <summary>Gets <see cref="ThrottledTimeNanos"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ThrottledTime => TimeSpan.FromTicks(ThrottledTimeNanos / 100);
}
