using System;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One healthcheck run.</summary>
public sealed class HealthCheckEntry
{
    /// <summary>Gets when the check started.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>Gets when the check finished.</summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>Gets the check's exit code.</summary>
    public long ExitCode { get; init; }

    /// <summary>Gets whatever the check printed.</summary>
    public string Output { get; init; } = string.Empty;
}
