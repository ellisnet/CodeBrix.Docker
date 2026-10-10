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
/// <para>
/// Arguments are passed through <see cref="ProcessStartInfo.ArgumentList"/> — never concatenated into
/// a shell string — and both output streams are drained concurrently to avoid pipe deadlocks.
/// </para>
/// <para>
/// One failure, and only one, is retried: the CLI stopping during its own start-up because another
/// process had one of its files open (see <see cref="IsTransientInitializationFailure"/>). Such an
/// attempt never connected to the daemon, so running the same command again is safe even for builds
/// and copies. Every other outcome is returned or raised on the first attempt.
/// </para>
/// </remarks>
internal sealed class DockerCliRunner
{
    /// <summary>
    /// The text the docker CLI writes to standard error immediately before it exits because it could not
    /// finish its own start-up (resolving the endpoint of its current context, creating its API client).
    /// </summary>
    internal const string InitializationFailureText = "Failed to initialize";

    /// <summary>
    /// The text of the Windows sharing-violation error (ERROR_SHARING_VIOLATION): another process has the
    /// file open in a mode that excludes this one.
    /// </summary>
    internal const string SharingViolationText = "being used by another process";

    /// <summary>
    /// The pauses taken before each extra attempt after a transient initialization failure. Three entries
    /// mean at most three retries, four attempts in all.
    /// </summary>
    internal static readonly IReadOnlyList<TimeSpan> TransientFailureRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
    ];

    private readonly DockerClientOptions _options;
    private readonly Func<IReadOnlyList<string>, string, IProgress<string>, CancellationToken, Task<CliResult>>
        _runAttempt;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerCliRunner"/> class.
    /// </summary>
    /// <param name="options">The client options supplying <see cref="DockerClientOptions.DockerCliPath"/>.</param>
    public DockerCliRunner(DockerClientOptions options)
        : this(options, runAttempt: null, retryDelays: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerCliRunner"/> class with a replaceable process
    /// launch and retry schedule, so that the retry loop can be tested without a docker executable.
    /// </summary>
    /// <param name="options">The client options supplying <see cref="DockerClientOptions.DockerCliPath"/>.</param>
    /// <param name="runAttempt">
    /// Runs one attempt to completion and returns its result, or <see langword="null"/> to start a real
    /// <c>docker</c> process.
    /// </param>
    /// <param name="retryDelays">
    /// The pause before each extra attempt, or <see langword="null"/> for
    /// <see cref="TransientFailureRetryDelays"/>. Its length is the maximum number of retries.
    /// </param>
    internal DockerCliRunner(DockerClientOptions options,
        Func<IReadOnlyList<string>, string, IProgress<string>, CancellationToken, Task<CliResult>> runAttempt,
        IReadOnlyList<TimeSpan> retryDelays)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runAttempt = runAttempt ?? RunProcessAsync;
        _retryDelays = retryDelays ?? TransientFailureRetryDelays;
    }

    /// <summary>
    /// Runs <c>docker</c> with the given arguments and throws when it exits non-zero.
    /// </summary>
    /// <param name="args">The argument list, one element per argument.</param>
    /// <param name="workingDir">The working directory, or <see langword="null"/> for the current one.</param>
    /// <param name="output">An optional receiver for interleaved stdout/stderr lines as they arrive.</param>
    /// <param name="cancellationToken">A token that kills the process when cancelled.</param>
    /// <returns>The captured result.</returns>
    /// <remarks>
    /// A transient initialization failure is retried first, as described on <see cref="TryRunAsync"/>;
    /// the exception describes the last attempt.
    /// </remarks>
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
    /// <returns>The captured result of the last attempt.</returns>
    /// <remarks>
    /// When an attempt fails with a transient initialization failure (see
    /// <see cref="IsTransientInitializationFailure"/>) the same command is run again after each pause in the
    /// retry schedule, which <paramref name="cancellationToken"/> also cancels. Lines an earlier attempt
    /// reported to <paramref name="output"/> stay reported, followed by one line announcing the retry; the
    /// returned result holds only the last attempt's exit code and streams.
    /// </remarks>
    /// <exception cref="DockerCliException">The process could not be started.</exception>
    public async Task<CliResult> TryRunAsync(IReadOnlyList<string> args, string workingDir = null,
        IProgress<string> output = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Nothing is ever written to the child's standard input (RedirectStandardInput is false), so every
        // attempt is an exact repeat of the first. Revisit this loop before feeding the CLI any input.
        var result = await _runAttempt(args, workingDir, output, cancellationToken).ConfigureAwait(false);

        for (var retry = 0;
             retry < _retryDelays.Count && IsTransientInitializationFailure(result.ExitCode, result.Stderr);
             retry++)
        {
            var delay = _retryDelays[retry];
            output?.Report(
                "The docker command line stopped during start-up because another process had one of its " +
                $"files open; retrying in {delay.TotalMilliseconds:0} ms (attempt {retry + 2} of " +
                $"{_retryDelays.Count + 1}).");

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            result = await _runAttempt(args, workingDir, output, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Decides whether a finished attempt failed only because the docker CLI could not open one of its own
    /// files while it was starting up, so that running the same command again is safe.
    /// </summary>
    /// <param name="exitCode">The attempt's exit code.</param>
    /// <param name="standardError">Everything the attempt wrote to standard error.</param>
    /// <returns>
    /// <see langword="true"/> when the attempt exited non-zero and its standard error contains both
    /// <see cref="InitializationFailureText"/> and <see cref="SharingViolationText"/>, compared ordinally
    /// and ignoring case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the failure Docker Desktop on Windows produces when another process — Docker Desktop
    /// itself, or another docker CLI — has the current context's metadata file open:
    /// <c>Failed to initialize: unable to resolve docker endpoint: context "desktop-linux": open
    /// ...\meta.json: The process cannot access the file because it is being used by another
    /// process.</c> The CLI writes "Failed to initialize" just before it exits during start-up, before it
    /// has any connection to the daemon, which is what makes a repeat safe even for a build or a copy.
    /// </para>
    /// <para>
    /// Both texts are required. The sharing-violation text alone can come from a step the CLI carries on
    /// past, and "unable to resolve docker endpoint" without "Failed to initialize" does not prove the CLI
    /// stopped there. The sharing-violation text is Windows' own wording and never appears on Linux or
    /// macOS, so the check needs no platform test and changes nothing on those platforms.
    /// </para>
    /// </remarks>
    internal static bool IsTransientInitializationFailure(int exitCode, string standardError) =>
        exitCode != 0
        && !string.IsNullOrEmpty(standardError)
        && standardError.Contains(InitializationFailureText, StringComparison.OrdinalIgnoreCase)
        && standardError.Contains(SharingViolationText, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts one <c>docker</c> process, drains both of its streams and waits for it to exit.
    /// </summary>
    private async Task<CliResult> RunProcessAsync(IReadOnlyList<string> args, string workingDir,
        IProgress<string> output, CancellationToken cancellationToken)
    {
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
