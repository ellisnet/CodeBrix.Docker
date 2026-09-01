namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a <c>docker</c> command-line invocation.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="Stdout">Everything the process wrote to standard output.</param>
/// <param name="Stderr">Everything the process wrote to standard error.</param>
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr);
