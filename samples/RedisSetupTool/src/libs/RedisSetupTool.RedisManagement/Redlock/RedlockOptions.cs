using System;

namespace RedisSetupTool.RedisManagement.Redlock;

/// <summary>How the lock service behaves.</summary>
public sealed class RedlockOptions
{
    /// <summary>Gets or sets how long a lock lives when no time is given.</summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how many times acquisition is retried.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Gets or sets the base delay between retries; the real delay is randomized.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets or sets the share of the lifetime allowed for clock drift.</summary>
    public double ClockDriftFactor { get; set; } = 0.01;

    /// <summary>Gets or sets the smallest drift allowance.</summary>
    public TimeSpan ClockDriftFloor { get; set; } = TimeSpan.FromMilliseconds(2);

    /// <summary>
    /// Gets or sets the per-node timeout. Null uses the algorithm's own rule - the smaller of a tenth
    /// of the lifetime and fifty milliseconds - so one dead node cannot eat the whole budget.
    /// </summary>
    public TimeSpan? NodeTimeout { get; set; }

    /// <summary>Works out the per-node timeout for a lifetime.</summary>
    /// <param name="ttl">How long the lock will live.</param>
    /// <returns>The timeout.</returns>
    public TimeSpan ResolveNodeTimeout(TimeSpan ttl)
    {
        if (NodeTimeout.HasValue)
        {
            return NodeTimeout.Value;
        }

        var tenth = TimeSpan.FromMilliseconds(ttl.TotalMilliseconds / 10d);
        var ceiling = TimeSpan.FromMilliseconds(50);
        return tenth < ceiling ? tenth : ceiling;
    }
}
