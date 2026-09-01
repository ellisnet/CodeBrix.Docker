using System;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>Whether the container met the out-of-memory killer.</summary>
public sealed class OomInfo
{
    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets a value indicating whether the container was killed for using too much memory.</summary>
    public bool WasOomKilled { get; init; }

    /// <summary>Gets the last exit code.</summary>
    public long ExitCode { get; init; }

    /// <summary>Gets how many times the container has restarted.</summary>
    public long RestartCount { get; init; }

    /// <summary>Gets when the container last stopped.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets the memory limit that was in force, in bytes.</summary>
    public long? MemoryLimitBytes { get; init; }

    /// <summary>Gets a sentence explaining the numbers.</summary>
    public string Interpretation { get; init; } = string.Empty;
}
