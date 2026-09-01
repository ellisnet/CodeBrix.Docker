namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a command run inside a container.
/// </summary>
/// <param name="Stdout">Everything the command wrote to standard output.</param>
/// <param name="Stderr">Everything the command wrote to standard error.</param>
/// <param name="ExitCode">The command's exit code.</param>
public sealed record ExecResult(string Stdout, string Stderr, long ExitCode)
{
    /// <summary>Gets a value indicating whether the command exited zero.</summary>
    public bool Succeeded => ExitCode == 0;
}
