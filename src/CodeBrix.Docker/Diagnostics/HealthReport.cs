using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// The state of a container's healthcheck, including the most recent probe results.
/// </summary>
public sealed class HealthReport
{
    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the container has a healthcheck at all — from the image's
    /// <c>HEALTHCHECK</c> instruction or from <see cref="ContainerSpec.Healthcheck"/>. Without one the
    /// daemon reports the container as up the moment its process starts, whether or not it can serve.
    /// </summary>
    public bool HasHealthcheck { get; init; }

    /// <summary>
    /// Gets the health status — <c>starting</c>, <c>healthy</c>, <c>unhealthy</c> or <c>none</c> — or
    /// <see langword="null"/> when the container has no healthcheck.
    /// </summary>
    public string Status { get; init; }

    /// <summary>Gets the number of consecutive probe failures so far.</summary>
    public long FailingStreak { get; init; }

    /// <summary>Gets the most recent recorded probe runs, oldest first.</summary>
    public IReadOnlyList<ContainerHealthLogEntry> RecentLogs { get; init; } = [];

    /// <summary>Gets a one-sentence, human-readable reading of the state above.</summary>
    public string Interpretation { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the container is currently reporting healthy.</summary>
    public bool IsHealthy => string.Equals(Status, "healthy", StringComparison.OrdinalIgnoreCase);
}
