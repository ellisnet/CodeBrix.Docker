namespace CodeBrix.Docker;

/// <summary>
/// Which of a container's output streams a chunk read from a
/// <see cref="ContainerExecStream"/> came from.
/// </summary>
/// <remarks>
/// A TTY exec has only one stream — the pseudo-terminal merges standard output and standard error —
/// so every chunk of a TTY session reports <see cref="StandardOutput"/>. The daemon's fourth stream
/// identifier, the one it uses to report its own failures, never reaches the caller as a target: it
/// is raised as a <see cref="DockerException"/> instead.
/// </remarks>
public enum ExecStreamTarget
{
    /// <summary>No data. Reported once the stream has ended.</summary>
    None,

    /// <summary>The command's standard output.</summary>
    StandardOutput,

    /// <summary>The command's standard error. Never reported for a TTY exec.</summary>
    StandardError,
}
