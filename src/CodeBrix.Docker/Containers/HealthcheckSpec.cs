using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A container healthcheck. Used both when creating a container and when reading one back from
/// <c>Config.Healthcheck</c>.
/// </summary>
public sealed class HealthcheckSpec
{
    /// <summary>
    /// Gets or sets the test to run, in the daemon's array form — for example
    /// <c>["CMD-SHELL", "curl -f http://localhost/ || exit 1"]</c>. <c>["NONE"]</c> disables an
    /// inherited healthcheck.
    /// </summary>
    [JsonPropertyName("Test")]
    public IReadOnlyList<string> Test { get; set; }

    /// <summary>Gets or sets how often the test runs.</summary>
    [JsonPropertyName("Interval")]
    [JsonConverter(typeof(NanosecondTimeSpanConverter))]
    public TimeSpan? Interval { get; set; }

    /// <summary>Gets or sets how long a single test may take before it counts as failed.</summary>
    [JsonPropertyName("Timeout")]
    [JsonConverter(typeof(NanosecondTimeSpanConverter))]
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the grace period after start during which failures do not count against
    /// <see cref="Retries"/>.
    /// </summary>
    [JsonPropertyName("StartPeriod")]
    [JsonConverter(typeof(NanosecondTimeSpanConverter))]
    public TimeSpan? StartPeriod { get; set; }

    /// <summary>Gets or sets the number of consecutive failures that mark the container unhealthy.</summary>
    [JsonPropertyName("Retries")]
    public int? Retries { get; set; }
}
