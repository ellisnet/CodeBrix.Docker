using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Reaches a remote daemon the way Docker's own CLI does: by running the system SSH client with
/// <c>docker system dial-stdio</c> on the far end, which proxies its standard input and output to the
/// remote <c>/var/run/docker.sock</c>. The Engine API then speaks ordinary HTTP over that pipe.
/// </summary>
/// <remarks>
/// <para>
/// Everything to do with keys, agents, <c>~/.ssh/config</c>, jump hosts and <c>known_hosts</c> belongs
/// to OpenSSH and is deliberately left there. CodeBrix.Docker adds exactly two things of its own:
/// <c>BatchMode=yes</c>, so that nothing can ever stop to ask a question a library cannot answer, and a
/// connect timeout. Because OpenSSH honours the first value it is given for an option, and these are
/// passed first, no later argument can reintroduce a prompt.
/// </para>
/// <para>
/// Host keys are never accepted automatically. An unknown or changed host key fails under
/// <c>BatchMode</c>, and the failure is reported as advice to connect once by hand;
/// <c>StrictHostKeyChecking=no</c> is a genuine security downgrade and is not a default here.
/// </para>
/// </remarks>
internal static class SshDialStdioConnection
{
    /// <summary>The connect timeout used when the client has no positive default timeout.</summary>
    private const int FallbackConnectTimeoutSeconds = 30;

    /// <summary>
    /// Starts the SSH client and returns the daemon connection its standard streams carry.
    /// </summary>
    /// <param name="endpoint">The parsed <c>ssh://</c> endpoint.</param>
    /// <param name="options">The client options supplying the SSH client and its extra arguments.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A connected stream. The caller owns its lifetime, and disposing it kills the child.</returns>
    /// <exception cref="DockerException">The SSH client could not be started.</exception>
    public static ValueTask<Stream> ConnectAsync(DockerEndpoint endpoint, DockerClientOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var executable = string.IsNullOrWhiteSpace(options.SshExecutablePath)
            ? "ssh"
            : options.SshExecutablePath.Trim();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in BuildArguments(endpoint, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new DockerException(
                $"Could not start the SSH client '{executable}' for endpoint '{endpoint.Original}'. The ssh:// " +
                "transport runs the system SSH client, which must be installed and on PATH — OpenSSH ships " +
                "with Linux, macOS and Windows 10 and later. Set DockerClientOptions.SshExecutablePath to name " +
                $"a different one. The process could not be started: {ex.Message}", ex);
        }

        return ValueTask.FromResult<Stream>(new SshProcessStream(process, endpoint, executable));
    }

    /// <summary>
    /// Builds the SSH client's argument list. Arguments are passed one per element and never through a
    /// shell, and the destination is separated from them by <c>--</c> so that a host name can never be
    /// read as an option.
    /// </summary>
    /// <param name="endpoint">The parsed <c>ssh://</c> endpoint.</param>
    /// <param name="options">The client options.</param>
    /// <returns>The argument list, ending in the remote command.</returns>
    public static IReadOnlyList<string> BuildArguments(DockerEndpoint endpoint, DockerClientOptions options)
    {
        var arguments = new List<string>
        {
            // Nothing may prompt: a library has nowhere to put a password or a host-key question, and a
            // prompt with nowhere to go is a hang. OpenSSH takes the first value given for an option, so
            // this one cannot be overridden by anything below.
            "-o", "BatchMode=yes",
            "-o", string.Create(CultureInfo.InvariantCulture, $"ConnectTimeout={ConnectTimeoutSeconds(options)}"),

            // The stream carries Engine API traffic, not a session, so no pseudo-terminal.
            "-T",
        };

        if (endpoint.UserName.Length > 0)
        {
            arguments.Add("-l");
            arguments.Add(endpoint.UserName);
        }

        // Only when the endpoint asks for something other than the default, so that a Port in the user's
        // ssh_config still applies to 'ssh://host'.
        if (endpoint.Port != DockerEndpoint.DefaultSshPort)
        {
            arguments.Add("-p");
            arguments.Add(endpoint.Port.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var argument in options.SshArguments ?? [])
        {
            if (!string.IsNullOrEmpty(argument))
            {
                arguments.Add(argument);
            }
        }

        arguments.Add("--");
        arguments.Add(endpoint.Host);

        // The far end proxies its standard streams to the daemon socket. This is the same command
        // Docker's own CLI runs, and it is why the remote host needs the Docker CLI installed.
        arguments.Add("docker");
        arguments.Add("system");
        arguments.Add("dial-stdio");

        return arguments;
    }

    /// <summary>Describes an endpoint for an error message, for example <c>'root@host' on port 2222</c>.</summary>
    /// <param name="endpoint">The parsed <c>ssh://</c> endpoint.</param>
    /// <returns>The description.</returns>
    public static string Describe(DockerEndpoint endpoint) =>
        $"'{endpoint.SshDestination}' on port {endpoint.Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Turns an SSH client that exited badly into an exception that names the cause, rather than leaving
    /// the caller with a connection that stopped answering.
    /// </summary>
    /// <param name="endpoint">The parsed <c>ssh://</c> endpoint.</param>
    /// <param name="executablePath">The SSH client that ran.</param>
    /// <param name="exitCode">The client's exit code.</param>
    /// <param name="errorText">Everything the client wrote to standard error.</param>
    /// <returns>The exception to throw.</returns>
    public static DockerException CreateFailure(DockerEndpoint endpoint, string executablePath, int exitCode,
        string errorText)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var target = Describe(endpoint);
        var text = errorText ?? string.Empty;
        var reported = string.IsNullOrWhiteSpace(text) ? "nothing" : text.Trim().ReplaceLineEndings(" ");
        var suffix = $" The SSH client exited with code {exitCode.ToString(CultureInfo.InvariantCulture)} " +
                     $"and reported: {reported}";

        if (Mentions(text, "Host key verification failed", "host key is known",
                "REMOTE HOST IDENTIFICATION HAS CHANGED", "Host key for"))
        {
            return new DockerException(
                $"The SSH host key of {target} is not trusted, so the connection was refused. CodeBrix.Docker " +
                "never accepts a host key automatically. Connect once by hand — " +
                $"'{ManualCommand(executablePath, endpoint)}' — to check the key and record it in your " +
                $"known_hosts file, then try again.{suffix}");
        }

        if (exitCode == 127 || Mentions(text, "docker: not found", "docker: command not found",
                "'docker' is not recognized"))
        {
            return new DockerException(
                $"The remote host {target} has no 'docker' command on its PATH, so " +
                "'docker system dial-stdio' could not run. An ssh:// endpoint needs the Docker CLI installed " +
                "on the remote host — the same requirement Docker's own CLI has — and a login shell whose " +
                $"PATH finds it.{suffix}");
        }

        if (Mentions(text, "Permission denied", "no supported authentication methods",
                "Too many authentication failures", "Authentication failed"))
        {
            return new DockerException(
                $"SSH authentication to {target} failed. CodeBrix.Docker runs the SSH client with " +
                "BatchMode=yes, so key-based authentication is the only option and a password prompt fails " +
                "immediately instead of hanging. Load a usable key into your SSH agent, or name one through " +
                $"DockerClientOptions.SshArguments — for example [\"-i\", \"/path/to/key\"].{suffix}");
        }

        if (Mentions(text, "Could not resolve hostname", "Connection refused", "Connection timed out",
                "Network is unreachable", "No route to host", "Operation timed out"))
        {
            return new DockerException(
                $"The SSH connection to {target} could not be established. Check that the host is reachable " +
                $"and that its SSH service is listening on that port.{suffix}");
        }

        return new DockerException($"The ssh:// connection to {target} failed.{suffix}");
    }

    /// <summary>Builds the command a user should run by hand to record a host key.</summary>
    private static string ManualCommand(string executablePath, DockerEndpoint endpoint)
    {
        var client = string.IsNullOrWhiteSpace(executablePath) ? "ssh" : executablePath;
        var port = endpoint.Port == DockerEndpoint.DefaultSshPort
            ? string.Empty
            : $"-p {endpoint.Port.ToString(CultureInfo.InvariantCulture)} ";
        return $"{client} {port}{endpoint.SshDestination}";
    }

    /// <summary>Gets the connect timeout in whole seconds, derived from the client's default timeout.</summary>
    private static int ConnectTimeoutSeconds(DockerClientOptions options)
    {
        var timeout = options.DefaultTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            return FallbackConnectTimeoutSeconds;
        }

        var seconds = Math.Ceiling(timeout.TotalSeconds);
        return seconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)seconds);
    }

    private static bool Mentions(string text, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
