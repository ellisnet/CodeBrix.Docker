namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The outcome of a one-shot command run inside a container.</summary>
public sealed class CommandResult
{
    /// <summary>Gets the standard output text.</summary>
    public string Stdout { get; init; } = string.Empty;

    /// <summary>Gets the standard error text.</summary>
    public string Stderr { get; init; } = string.Empty;

    /// <summary>Gets the process exit code.</summary>
    public long ExitCode { get; init; }

    /// <summary>Gets a value indicating whether the command exited zero.</summary>
    public bool Succeeded => ExitCode == 0;
}
