using System;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// One recorded healthcheck run.
/// </summary>
public sealed class ContainerHealthLogEntry
{
    /// <summary>Gets when the check started.</summary>
    [JsonPropertyName("Start")]
    public DateTimeOffset? Start { get; init; }

    /// <summary>Gets when the check finished.</summary>
    [JsonPropertyName("End")]
    public DateTimeOffset? End { get; init; }

    /// <summary>Gets the check's exit code; <c>0</c> means healthy.</summary>
    [JsonPropertyName("ExitCode")]
    public long ExitCode { get; init; }

    /// <summary>Gets whatever the check wrote to its output streams.</summary>
    [JsonPropertyName("Output")]
    public string Output { get; init; }
}
