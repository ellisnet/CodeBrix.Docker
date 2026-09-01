using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The healthcheck state of a container, present only when the image or container defines one.
/// </summary>
public sealed class ContainerHealth
{
    /// <summary>
    /// Gets the health status: <c>starting</c>, <c>healthy</c>, <c>unhealthy</c> or <c>none</c>.
    /// </summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; }

    /// <summary>Gets the number of consecutive failures so far.</summary>
    [JsonPropertyName("FailingStreak")]
    public long FailingStreak { get; init; }

    /// <summary>Gets the most recent healthcheck runs, oldest first.</summary>
    [JsonPropertyName("Log")]
    public IReadOnlyList<ContainerHealthLogEntry> Log { get; init; }

    /// <summary>Gets a value indicating whether the container is currently healthy.</summary>
    [JsonIgnore]
    public bool IsHealthy => string.Equals(Status, "healthy", StringComparison.OrdinalIgnoreCase);
}
