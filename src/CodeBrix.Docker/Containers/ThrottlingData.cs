using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// CPU throttling counters from <c>cpu.stat</c> — the evidence that a CPU limit is actually biting.
/// </summary>
public sealed class ThrottlingData
{
    /// <summary>Gets the number of CFS scheduling periods the container has been through.</summary>
    [JsonPropertyName("periods")]
    public long? Periods { get; init; }

    /// <summary>Gets how many of those periods ended with the container throttled.</summary>
    [JsonPropertyName("throttled_periods")]
    public long? ThrottledPeriods { get; init; }

    /// <summary>Gets the total time the container spent throttled, in nanoseconds.</summary>
    [JsonPropertyName("throttled_time")]
    public long? ThrottledTime { get; init; }

    /// <summary>
    /// Gets the fraction of scheduling periods in which the container was throttled, between 0 and 1.
    /// Returns <see langword="null"/> when the daemon reported no periods (an exited container).
    /// </summary>
    /// <returns>The throttle ratio.</returns>
    public double? ThrottleRatio()
    {
        if (Periods is null)
        {
            return null;
        }

        if (Periods.Value <= 0)
        {
            return 0d;
        }

        return ThrottledPeriods is null ? null : (double)ThrottledPeriods.Value / Periods.Value;
    }
}
