using System;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How much CPU time the kernel took away from a container.</summary>
public sealed class CpuThrottlingInfo
{
    /// <summary>Gets a value indicating whether the daemon supplied usable figures.</summary>
    public bool HasLiveData { get; init; }

    /// <summary>Gets the number of enforcement periods.</summary>
    public long Periods { get; init; }

    /// <summary>Gets how many periods ended in throttling.</summary>
    public long ThrottledPeriods { get; init; }

    /// <summary>Gets the total time spent throttled.</summary>
    public TimeSpan ThrottledTime { get; init; }

    /// <summary>Gets the share of periods that were throttled.</summary>
    public double ThrottleRatio { get; init; }

    /// <summary>Gets how bad the throttling is.</summary>
    public ThrottleLevel Severity { get; init; }

    /// <summary>Gets a sentence explaining the numbers.</summary>
    public string Interpretation { get; init; } = string.Empty;
}
