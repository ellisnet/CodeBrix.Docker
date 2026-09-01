using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>
/// Finds a shell inside a container by running each candidate and reading the exit code. A missing
/// binary does not throw and does not hang: the daemon still upgrades the connection, writes the
/// runtime's message on the ordinary output stream, closes, and reports exit code 127.
/// </summary>
/// <remarks>
/// The probe itself takes a CodeBrix.Docker client, so it is internal; the operation is public
/// through <see cref="IDockerManager.ProbeShellAsync"/>, which keeps the seam intact.
/// </remarks>
public static class ShellProber
{
    /// <summary>The shells tried, in order, when a caller supplies no list of its own.</summary>
    public static IReadOnlyList<string> DefaultCandidates { get; } =
        ["/bin/bash", "/bin/sh", "/bin/ash", "/bin/busybox"];

    /// <summary>The exit code the runtime reports when the binary does not exist.</summary>
    public const int MissingBinaryExitCode = 127;

    /// <summary>The message fragment the runtime writes when the binary does not exist.</summary>
    public const string MissingBinaryMarker = "OCI runtime exec failed";

    internal static async Task<ShellProbeResult> ProbeAsync(DockerClient client, string idOrName,
        IReadOnlyList<string> candidates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrName);

        var tried = new List<string>();
        var lastMessage = string.Empty;

        foreach (var candidate in candidates is { Count: > 0 } ? candidates : DefaultCandidates)
        {
            tried.Add(candidate);

            var command = candidate.EndsWith("busybox", StringComparison.Ordinal)
                ? new[] { candidate, "sh", "-c", "exit 0" }
                : [candidate, "-c", "exit 0"];

            var spec = new ExecSpec
            {
                Command = command,
                AttachStdin = false,
                AttachStdout = true,
                AttachStderr = true,
                Tty = false,
            };

            await using var stream = await client.Containers
                .ExecStreamAsync(idOrName, spec, cancellationToken).ConfigureAwait(false);

            var transcript = await stream.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var exitCode = await stream.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var combined = transcript.Combined;

            if (exitCode == MissingBinaryExitCode
                || combined.Contains(MissingBinaryMarker, StringComparison.Ordinal))
            {
                lastMessage = combined.Trim();
                continue;
            }

            return new ShellProbeResult { Found = true, ShellPath = candidate, Tried = tried };
        }

        return new ShellProbeResult { Found = false, Tried = tried, Message = lastMessage };
    }

    internal static string DescribeFailure(string image, ShellProbeResult result) =>
        $"{image} has none of {string.Join(", ", result.Tried)}. "
        + "A distroless image has no shell to open a console into."
        + (string.IsNullOrEmpty(result.Message) ? string.Empty : " " + result.Message);
}
