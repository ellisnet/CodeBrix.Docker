using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>Which shell a container turned out to have.</summary>
public sealed class ShellProbeResult
{
    /// <summary>Gets a value indicating whether one of the candidates exists.</summary>
    public bool Found { get; init; }

    /// <summary>Gets the shell that worked.</summary>
    public string ShellPath { get; init; }

    /// <summary>Gets the candidates that were tried, in order; never null.</summary>
    public IReadOnlyList<string> Tried { get; init; } = [];

    /// <summary>Gets the last message the runtime produced, when nothing worked.</summary>
    public string Message { get; init; }
}
