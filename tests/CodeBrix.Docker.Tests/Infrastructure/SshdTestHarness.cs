using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// A containerised OpenSSH server standing in for a remote Docker host, so that the <c>ssh://</c>
/// transport can be exercised end to end without an SSH daemon on the workstation and without touching
/// the developer's own <c>~/.ssh</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two images are built from one Dockerfile: a base carrying <c>sshd</c>, and a second stage that adds
/// the Docker CLI. A container from the second gets <c>/var/run/docker.sock</c> bind-mounted, so
/// <c>docker system dial-stdio</c> there reaches the very daemon the test suite is talking to — the
/// complete path is ssh client → <c>dial-stdio</c> → mounted socket → daemon. A container from the
/// first has no <c>docker</c> command at all, which is the "missing remote CLI" case.
/// </para>
/// <para>
/// The throwaway key pair, the <c>known_hosts</c> file naming both containers, and an empty
/// <c>known_hosts</c> for the untrusted-host case all live in a temporary directory that this instance
/// owns. Everything on the daemon carries the suite's label, so the fixture's sweep removes it.
/// </para>
/// </remarks>
public sealed class SshdTestHarness : IDisposable
{
    /// <summary>The image tag carrying <c>sshd</c> and the Docker CLI.</summary>
    public const string ImageWithDockerCli = DockerTestFixture.ImageRepositoryPrefix + "sshd:with-docker-cli";

    /// <summary>The image tag carrying <c>sshd</c> and no Docker CLI.</summary>
    public const string ImageWithoutDockerCli = DockerTestFixture.ImageRepositoryPrefix + "sshd:no-docker-cli";

    /// <summary>The user the containers accept, and the one the tests connect as.</summary>
    public const string UserName = "root";

    private const string WithDockerCliStage = "sshd-with-docker-cli";
    private const string WithoutDockerCliStage = "sshd-base";
    private const int FirstCandidatePort = 2222;
    private const int CandidatePortCount = 20;

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    private readonly TempDirectory _workspace;

    private SshdTestHarness(TempDirectory workspace) => _workspace = workspace;

    /// <summary>Gets the endpoint of the container that has the Docker CLI and the mounted socket.</summary>
    public string Endpoint { get; private init; }

    /// <summary>Gets the endpoint of the container that has no Docker CLI.</summary>
    public string EndpointWithoutDockerCli { get; private init; }

    /// <summary>Gets the throwaway private key the containers authorize.</summary>
    public string IdentityFilePath { get; private init; }

    /// <summary>Gets a <c>known_hosts</c> file naming the host keys of both containers.</summary>
    public string KnownHostsPath { get; private init; }

    /// <summary>Gets an empty <c>known_hosts</c> file, for which neither container is trusted.</summary>
    public string EmptyKnownHostsPath { get; private init; }

    /// <summary>
    /// Builds the images, starts both containers and records their host keys.
    /// </summary>
    /// <param name="fixture">The suite fixture, which supplies the client, names and labels.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The started harness.</returns>
    public static async Task<SshdTestHarness> StartAsync(DockerTestFixture fixture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var workspace = new TempDirectory();

        try
        {
            var identityFile = Path.Combine(workspace.Path, "id_ed25519");
            await GenerateKeyPairAsync(identityFile, cancellationToken);
            workspace.WriteFile("authorized_keys", await File.ReadAllTextAsync(identityFile + ".pub",
                cancellationToken));
            workspace.WriteFile("Dockerfile", Dockerfile);

            await BuildAsync(fixture, workspace.Path, WithDockerCliStage, ImageWithDockerCli, cancellationToken);
            await BuildAsync(fixture, workspace.Path, WithoutDockerCliStage, ImageWithoutDockerCli,
                cancellationToken);

            var withCliPort = FindFreePort(FirstCandidatePort);
            var withoutCliPort = FindFreePort(withCliPort + 1);

            var withCli = await RunAsync(fixture, "sshd", ImageWithDockerCli, withCliPort, mountSocket: true,
                cancellationToken);
            var withoutCli = await RunAsync(fixture, "sshd-no-cli", ImageWithoutDockerCli, withoutCliPort,
                mountSocket: false, cancellationToken);

            var knownHosts = new StringBuilder()
                .AppendLine(await ReadHostKeyLineAsync(fixture, withCli, withCliPort, cancellationToken))
                .AppendLine(await ReadHostKeyLineAsync(fixture, withoutCli, withoutCliPort, cancellationToken))
                .ToString();

            return new SshdTestHarness(workspace)
            {
                Endpoint = $"ssh://{UserName}@localhost:{withCliPort}",
                EndpointWithoutDockerCli = $"ssh://{UserName}@localhost:{withoutCliPort}",
                IdentityFilePath = identityFile,
                KnownHostsPath = workspace.WriteFile("known_hosts", knownHosts),
                EmptyKnownHostsPath = workspace.WriteFile("empty_known_hosts", string.Empty),
            };
        }
        catch (Exception)
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds the client options for an endpoint, pointing the SSH client at this harness's key and at a
    /// scratch <c>known_hosts</c> file rather than anything belonging to the developer.
    /// </summary>
    /// <param name="endpoint">The endpoint to connect to.</param>
    /// <param name="knownHostsPath">
    /// The <c>known_hosts</c> file to trust, or <see langword="null"/> for the one naming both containers.
    /// </param>
    /// <returns>The options.</returns>
    public DockerClientOptions CreateOptions(string endpoint, string knownHostsPath = null) => new()
    {
        Endpoint = endpoint,
        SshArguments = CreateSshArguments(knownHostsPath ?? KnownHostsPath),
    };

    /// <summary>
    /// Builds the SSH arguments that isolate a test run from the developer's own SSH configuration.
    /// </summary>
    /// <param name="knownHostsPath">The <c>known_hosts</c> file to trust.</param>
    /// <returns>The arguments.</returns>
    public IList<string> CreateSshArguments(string knownHostsPath) =>
    [
        // Never read ~/.ssh/config, never consult the user's own known_hosts, never offer the user's keys.
        "-F", "/dev/null",
        "-o", $"UserKnownHostsFile={knownHostsPath}",
        "-o", "GlobalKnownHostsFile=/dev/null",
        "-o", "IdentitiesOnly=yes",
        "-i", IdentityFilePath,
    ];

    /// <inheritdoc />
    public void Dispose() => _workspace.Dispose();

    /// <summary>The two-stage Dockerfile: sshd on its own, then sshd plus the Docker CLI.</summary>
    private static string Dockerfile => """
        FROM alpine:3.19 AS sshd-base
        RUN apk add --no-cache openssh-server \
         && ssh-keygen -A \
         && mkdir -p /root/.ssh \
         && printf 'PermitRootLogin prohibit-password\nPasswordAuthentication no\n' >> /etc/ssh/sshd_config
        COPY authorized_keys /root/.ssh/authorized_keys
        RUN chmod 700 /root/.ssh && chmod 600 /root/.ssh/authorized_keys
        EXPOSE 22
        CMD ["/usr/sbin/sshd", "-D", "-e"]

        FROM sshd-base AS sshd-with-docker-cli
        RUN apk add --no-cache docker-cli
        """;

    private static async Task BuildAsync(DockerTestFixture fixture, string contextDirectory, string target,
        string tag, CancellationToken cancellationToken)
    {
        await fixture.Client.Images.BuildAsync(new ImageBuildSpec
        {
            ContextDirectory = contextDirectory,
            Target = target,
            Tags = { tag },
            Labels = { [DockerTestFixture.LabelName] = DockerTestFixture.LabelValue },
        }, cancellationToken);
    }

    private static async Task<string> RunAsync(DockerTestFixture fixture, string role, string image, int hostPort,
        bool mountSocket, CancellationToken cancellationToken)
    {
        var spec = fixture.Spec(role, image);
        spec.PortBindings.Add(new PortBinding(22, hostPort));
        if (mountSocket)
        {
            spec.Mounts.Add(MountSpec.Bind("/var/run/docker.sock", "/var/run/docker.sock"));
        }

        var id = await fixture.Client.Containers.RunAsync(spec, cancellationToken);

        await Poll.UntilTrueAsync(_ => Task.FromResult(CanConnect(hostPort)), TimeSpan.FromSeconds(60),
            $"sshd in container {role} to accept connections on port {hostPort}",
            TimeSpan.FromMilliseconds(250), cancellationToken);

        return id;
    }

    /// <summary>
    /// Reads a container's own host key and formats it as a <c>known_hosts</c> line for its published
    /// port, which is how the tests trust that container without accepting anything automatically.
    /// </summary>
    private static async Task<string> ReadHostKeyLineAsync(DockerTestFixture fixture, string containerId,
        int hostPort, CancellationToken cancellationToken)
    {
        var result = await fixture.Client.Containers.ExecAsync(containerId,
            ["cat", "/etc/ssh/ssh_host_ed25519_key.pub"], cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not read the host key of container {containerId}: {result.Stderr}");
        }

        var parts = result.Stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException(
                $"Container {containerId} returned an unreadable host key: '{result.Stdout}'.");
        }

        return $"[localhost]:{hostPort} {parts[0]} {parts[1]}";
    }

    private static async Task GenerateKeyPairAsync(string identityFile, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh-keygen",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
                 {
                     "-t", "ed25519", "-N", string.Empty, "-C", "codebrix-docker-tests", "-f", identityFile, "-q",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ssh-keygen could not be started.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ProcessTimeout);

        var stderr = await process.StandardError.ReadToEndAsync(deadline.Token);
        await process.WaitForExitAsync(deadline.Token);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ssh-keygen exited with code {process.ExitCode}: {stderr}");
        }
    }

    /// <summary>Finds a free loopback port at or above <paramref name="firstCandidate"/>.</summary>
    private static int FindFreePort(int firstCandidate)
    {
        for (var port = firstCandidate; port < firstCandidate + CandidatePortCount; port++)
        {
            if (!CanConnect(port) && CanListen(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"No free port was found between {firstCandidate} and {firstCandidate + CandidatePortCount - 1}.");
    }

    private static bool CanListen(int port)
    {
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    private static bool CanConnect(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
