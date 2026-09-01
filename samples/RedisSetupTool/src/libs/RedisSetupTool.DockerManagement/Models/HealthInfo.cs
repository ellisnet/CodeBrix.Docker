using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A container's healthcheck state.</summary>
public sealed class HealthInfo
{
    /// <summary>Gets a value indicating whether the image declares a healthcheck.</summary>
    public bool HasHealthcheck { get; init; }

    /// <summary>Gets the status word, for example <c>healthy</c>.</summary>
    public string Status { get; init; }

    /// <summary>Gets the number of consecutive failures.</summary>
    public long FailingStreak { get; init; }

    /// <summary>Gets a value indicating whether the check currently passes.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Gets a sentence explaining the state.</summary>
    public string Interpretation { get; init; } = string.Empty;

    /// <summary>Gets the most recent check runs; never null.</summary>
    public IReadOnlyList<HealthCheckEntry> RecentChecks { get; init; } = [];
}
