using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Everything needed to start a streaming exec session inside a running container. Only
/// <see cref="Command"/> is required.
/// </summary>
/// <example>
/// <code>
/// var spec = new ExecSpec
/// {
///     Command = ["/bin/sh"],
///     AttachStdin = true,
///     Tty = true,
///     ConsoleHeight = 24,
///     ConsoleWidth = 80,
/// };
///
/// await using var shell = await client.Containers.ExecStreamAsync("my-container", spec);
/// </code>
/// </example>
public sealed class ExecSpec
{
    /// <summary>
    /// Gets or sets the command and its arguments, for example <c>["/bin/sh"]</c>. Required.
    /// </summary>
    public IReadOnlyList<string> Command { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether standard input is attached. Required for an
    /// interactive session; without it the command sees end of file immediately.
    /// </summary>
    public bool AttachStdin { get; set; }

    /// <summary>Gets or sets a value indicating whether standard output is attached. Defaults to <see langword="true"/>.</summary>
    public bool AttachStdout { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether standard error is attached. Defaults to <see langword="true"/>.</summary>
    public bool AttachStderr { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the daemon allocates a pseudo-terminal inside the
    /// container for this command.
    /// </summary>
    /// <remarks>
    /// A TTY session gets a real terminal: the shell prints its prompt, emits ANSI escape sequences,
    /// echoes what is typed, ends its lines with CRLF, and merges standard error into standard
    /// output. Its bytes arrive verbatim rather than in <c>stdcopy</c> frames, so the two streams can
    /// no longer be told apart. Without a TTY there is no prompt, no echo, and the two streams stay
    /// separate.
    /// </remarks>
    public bool Tty { get; set; }

    /// <summary>Gets or sets the initial terminal height in rows. Applies only when <see cref="Tty"/> is set.</summary>
    public int? ConsoleHeight { get; set; }

    /// <summary>Gets or sets the initial terminal width in columns. Applies only when <see cref="Tty"/> is set.</summary>
    public int? ConsoleWidth { get; set; }

    /// <summary>Gets or sets the user to run as, or <see langword="null"/> for the container's default.</summary>
    public string User { get; set; }

    /// <summary>Gets or sets the working directory, or <see langword="null"/> for the container's default.</summary>
    public string WorkingDir { get; set; }

    /// <summary>Gets or sets extra environment variables, each in <c>KEY=VALUE</c> form.</summary>
    public IList<string> Env { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the command runs with extended privileges.</summary>
    public bool Privileged { get; set; }

    /// <summary>
    /// Throws when the specification cannot be sent to the daemon.
    /// </summary>
    /// <param name="parameterName">The name of the parameter carrying this specification.</param>
    /// <exception cref="ArgumentException">The specification is incomplete.</exception>
    internal void Validate(string parameterName)
    {
        if (Command is null || Command.Count == 0)
        {
            throw new ArgumentException("ExecSpec.Command must contain at least one element.", parameterName);
        }

        if (!AttachStdin && !AttachStdout && !AttachStderr)
        {
            throw new ArgumentException(
                "ExecSpec must attach at least one of standard input, standard output or standard error.",
                parameterName);
        }

        if (ConsoleHeight is <= 0 || ConsoleWidth is <= 0)
        {
            throw new ArgumentException("ExecSpec console dimensions must be greater than zero.", parameterName);
        }
    }
}
