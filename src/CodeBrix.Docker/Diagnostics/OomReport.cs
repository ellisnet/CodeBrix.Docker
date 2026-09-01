using System;

namespace CodeBrix.Docker;

/// <summary>
/// Whether the kernel's out-of-memory killer terminated a container. This is the one diagnostic that
/// is most useful <em>after</em> the container has stopped.
/// </summary>
public sealed class OomReport
{
    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the container is currently running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Gets a value indicating whether the daemon recorded an OOM kill for the container's last run.
    /// </summary>
    public bool WasOomKilled { get; init; }

    /// <summary>
    /// Gets the exit code of the last run. <c>137</c> is <c>128 + SIGKILL</c>, which combined with
    /// <see cref="WasOomKilled"/> is the definitive OOM-kill signature.
    /// </summary>
    public long ExitCode { get; init; }

    /// <summary>
    /// Gets how many times the daemon has restarted the container. A climbing count alongside
    /// <see cref="WasOomKilled"/> means a crash loop, not a one-off.
    /// </summary>
    public long RestartCount { get; init; }

    /// <summary>Gets when the container last exited, or <see langword="null"/> when it has not.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets the container's configured hard memory limit in bytes, when one is set.</summary>
    public long? MemoryLimitBytes { get; init; }

    /// <summary>Gets a one-sentence, human-readable reading of the state above.</summary>
    public string Interpretation { get; init; } = string.Empty;
}
