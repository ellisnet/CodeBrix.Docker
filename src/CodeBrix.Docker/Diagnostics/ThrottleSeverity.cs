namespace CodeBrix.Docker;

/// <summary>
/// How badly a container's CPU quota is constraining it, derived from the fraction of CFS scheduling
/// periods that ended with the container throttled.
/// </summary>
public enum ThrottleSeverity
{
    /// <summary>Fewer than 5% of scheduling periods were throttled — the CPU allowance is adequate.</summary>
    None = 0,

    /// <summary>Between 5% and 25% of periods were throttled — noticeable latency spikes are likely.</summary>
    Moderate = 1,

    /// <summary>Between 25% and 75% of periods were throttled — the CPU limit is materially hurting throughput.</summary>
    High = 2,

    /// <summary>More than 75% of periods were throttled — the container spends most of its time stalled.</summary>
    Critical = 3,
}
