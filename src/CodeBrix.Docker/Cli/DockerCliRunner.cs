using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Runs the <c>docker</c> command line for the few operations the Engine API cannot cover
/// (BuildKit builds, credential-helper authenticated pulls).
/// </summary>
/// <remarks>
/// Arguments are passed through <see cref="ProcessStartInfo.ArgumentList"/> — never concatenated into
/// a shell string — and both output streams are drained concurrently to avoid pipe deadlocks.
/// </remarks>
internal sealed class DockerCliRunner
{
    private readonly DockerClientOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerCliRunner"/> class.
    /// </summary>
    /// <param name="options">The client options supplying <see cref="DockerClientOptions.DockerCliPath"/>.</param>
    public DockerCliRunner(DockerClientOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Runs <c>docker</c> with the given arguments and throws when it exits non-zero.
    /// </summary>
    /// <param name="args">The argument list, one element per argument.</param>
    /// <param name="workingDir">The working directory, or <see langword="null"/> for the current one.</param>
    /// <param name="output">An optional receiver for interleaved stdout/stderr lines as they arrive.</param>
    /// <param name="cancellationToken">A token that kills the process when cancelled.</param>
    /// <returns>The captured result.</returns>
    /// <exception cref="DockerCliException">The process could not start, or exited non-zero.</exception>
    public async Task<CliResult> RunAsync(IReadOnlyList<string> args, string workingDir = null,
        IProgress<string> output = null, CancellationToken cancellationToken = default)
    {
        var result = await TryRunAsync(args, workingDir, output, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new DockerCliException(Describe(args), result.ExitCode,
                result.Stderr.Length > 0 ? result.Stderr : result.Stdout);
        }

        return result;
    }

    /// <summary>
    /// Runs <c>docker</c> with the given arguments and returns the result without throwing on a
    /// non-zero exit code. Useful for tools whose exit code encodes findings rather than failure.
    /// </summary>
    /// <param name="args">The argument list, one element per argument.</param>
    /// <param name="workingDir">The working directory, or <see langword="null"/> for the current one.</param>
    /// <param name="output">An optional receiver for interleaved stdout/stderr lines as they arrive.</param>
    /// <param name="cancellationToken">A token that kills the process when cancelled.</param>
    /// <returns>The captured result.</returns>
    /// <exception cref="DockerCliException">The process could not be started.</exception>
    public async Task<CliResult> TryRunAsync(IReadOnlyList<string> args, string workingDir = null,
        IProgress<string> output = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DockerCliPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrEmpty(workingDir))
        {
            startInfo.WorkingDirectory = workingDir;
        }

        // The CLI must act on the same daemon as the rest of the client. Without this, a client built on
        // an explicit endpoint — tcp://, or ssh:// to another host — would build and pull against
        // whatever the local environment points at instead. When no endpoint was configured, the child
        // inherits DOCKER_HOST and resolves it exactly as DockerEndpoint.Resolve does.
        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            startInfo.Environment["DOCKER_HOST"] = _options.Endpoint.Trim();
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) => Collect(e.Data, stdout, stdoutDone);
        process.ErrorDataReceived += (_, e) => Collect(e.Data, stderr, stderrDone);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new DockerCliException(Describe(args), -1,
                $"Could not start '{_options.DockerCliPath}': {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // Both readers signal completion when their stream reaches EOF, which can lag process exit.
        await Task.WhenAll(stdoutDone.Task, stderrDone.Task).ConfigureAwait(false);

        return new CliResult(process.ExitCode, stdout.ToString(), stderr.ToString());

        void Collect(string line, StringBuilder buffer, TaskCompletionSource completion)
        {
            if (line is null)
            {
                completion.TrySetResult();
                return;
            }

            lock (buffer)
            {
                buffer.AppendLine(line);
            }

            output?.Report(line);
        }
    }

    private string Describe(IReadOnlyList<string> args) =>
        $"{_options.DockerCliPath} {string.Join(' ', args)}";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already exited or cannot be killed; nothing useful to do.
        }
    }
}
