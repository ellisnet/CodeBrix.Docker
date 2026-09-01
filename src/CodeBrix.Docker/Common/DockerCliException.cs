using System;

namespace CodeBrix.Docker;

/// <summary>
/// Thrown when a shell-out to the <c>docker</c> command line fails.
/// </summary>
public sealed class DockerCliException : DockerException
{
    /// <summary>
    /// Gets the process exit code. A value of <c>-1</c> indicates the process could not be started.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets everything the process wrote to standard error.
    /// </summary>
    public string StdErr { get; }

    /// <summary>
    /// Gets the command line that was executed (executable plus arguments).
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerCliException"/> class.
    /// </summary>
    /// <param name="command">The command line that was executed.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="stdErr">Everything the process wrote to standard error.</param>
    /// <param name="innerException">The exception that caused this one, when the process failed to start.</param>
    public DockerCliException(string command, int exitCode, string stdErr, Exception innerException = null)
        : base(BuildMessage(command, exitCode, stdErr), innerException)
    {
        Command = command;
        ExitCode = exitCode;
        StdErr = stdErr ?? string.Empty;
    }

    private static string BuildMessage(string command, int exitCode, string stdErr)
    {
        var error = (stdErr ?? string.Empty).Trim();
        return error.Length == 0
            ? $"Command '{command}' failed with exit code {exitCode}."
            : $"Command '{command}' failed with exit code {exitCode}: {error}";
    }
}
