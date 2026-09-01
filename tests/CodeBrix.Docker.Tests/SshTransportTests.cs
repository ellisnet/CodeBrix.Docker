using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Exercises the <c>ssh://</c> transport: how endpoints parse, what the SSH client is actually asked to
/// do, and the complete path — ssh client to <c>docker system dial-stdio</c> to the mounted socket to
/// the daemon — against a containerised OpenSSH server, including the failures that must produce advice
/// rather than a stream that stopped answering.
/// </summary>
[Collection(DockerTestCollection.Name)]
public sealed class SshTransportTests(DockerTestFixture fixture, ITestOutputHelper output)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public void Parse_ForAnSshEndpoint_TakesTheUserHostAndPort()
    {
        //Arrange
        const string withEverything = "ssh://deploy@build-01.example.com:2222";
        const string withoutUserOrPort = "ssh://build-01.example.com";
        const string withIpv6Address = "ssh://deploy@[fe80::1]:2222";

        //Act
        var everything = DockerEndpoint.Parse(withEverything);
        var bare = DockerEndpoint.Parse(withoutUserOrPort);
        var ipv6 = DockerEndpoint.Parse(withIpv6Address);

        //Assert
        everything.Kind.Should().Be(DockerEndpointKind.Ssh);
        everything.UserName.Should().Be("deploy");
        everything.Host.Should().Be("build-01.example.com");
        everything.Port.Should().Be(2222);
        everything.SshDestination.Should().Be("deploy@build-01.example.com");

        // No user and no port: both are left to the SSH client's own configuration.
        bare.Kind.Should().Be(DockerEndpointKind.Ssh);
        bare.UserName.Should().BeEmpty();
        bare.Host.Should().Be("build-01.example.com");
        bare.Port.Should().Be(DockerEndpoint.DefaultSshPort);
        bare.SshDestination.Should().Be("build-01.example.com");

        ipv6.UserName.Should().Be("deploy");
        ipv6.Host.Should().Be("fe80::1");
        ipv6.Port.Should().Be(2222);
    }

    [Fact]
    public void Parse_ForAMalformedSshEndpoint_ExplainsWhatWasExpected()
    {
        //Arrange
        Action withPath = () => DockerEndpoint.Parse("ssh://deploy@host/var/run/docker.sock");
        Action withoutUser = () => DockerEndpoint.Parse("ssh://@host");
        Action withoutHost = () => DockerEndpoint.Parse("ssh://");

        //Act
        //Assert
        withPath.Should().Throw<DockerException>().WithMessage("*carries the path*");
        withoutUser.Should().Throw<DockerException>().WithMessage("*empty user name*");
        withoutHost.Should().Throw<DockerException>().WithMessage("*does not contain a host name*");

        // https:// stays unsupported: reaching a remote daemon is what ssh:// is for.
        Action tls = () => DockerEndpoint.Parse("https://host:2376");
        tls.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Resolve_TakesAnSshEndpointFromDockerHost()
    {
        //Arrange
        var original = Environment.GetEnvironmentVariable("DOCKER_HOST");

        try
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", "ssh://deploy@build-01:2222");

            //Act
            var fromEnvironment = DockerEndpoint.Parse(DockerEndpoint.Resolve(explicitEndpoint: null));
            var explicitWins = DockerEndpoint.Parse(DockerEndpoint.Resolve("unix:///var/run/docker.sock"));

            //Assert
            fromEnvironment.Kind.Should().Be(DockerEndpointKind.Ssh);
            fromEnvironment.SshDestination.Should().Be("deploy@build-01");
            fromEnvironment.Port.Should().Be(2222);

            // Precedence is unchanged: the option beats the environment variable.
            explicitWins.Kind.Should().Be(DockerEndpointKind.UnixSocket);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", original);
        }
    }

    [Fact]
    public void BuildArguments_AsksForBatchModeFirstAndNeverWeakensHostKeyChecking()
    {
        //Arrange
        var endpoint = DockerEndpoint.Parse("ssh://deploy@build-01:2222");
        var options = new DockerClientOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(45),
            SshArguments = ["-i", "/keys/deploy"],
        };

        //Act
        var arguments = SshDialStdioConnection.BuildArguments(endpoint, options).ToArray();

        //Assert
        // BatchMode comes first because OpenSSH honours the first value it is given for an option, so
        // nothing added afterwards can reintroduce a prompt that a library cannot answer.
        arguments.Take(4).Should().Equal("-o", "BatchMode=yes", "-o", "ConnectTimeout=45");
        arguments.Should().Contain("-T");
        arguments.Should().ContainInOrder("-l", "deploy");
        arguments.Should().ContainInOrder("-p", "2222");
        arguments.Should().ContainInOrder("-i", "/keys/deploy");

        // The destination is separated from the options, and the remote command is Docker's own.
        arguments.Should().ContainInOrder("--", "build-01", "docker", "system", "dial-stdio");

        // Host keys are OpenSSH's business, and nothing here waters that down.
        arguments.Should().NotContain(argument =>
            argument.Contains("StrictHostKeyChecking", StringComparison.OrdinalIgnoreCase));

        // A default port is left out, so that a Port in the user's ssh_config still applies.
        var defaultPort = SshDialStdioConnection
            .BuildArguments(DockerEndpoint.Parse("ssh://build-01"), new DockerClientOptions()).ToArray();
        defaultPort.Should().NotContain("-p");
        defaultPort.Should().NotContain("-l");
    }

    [Fact]
    public async Task GetVersionAsync_WhenTheSshClientIsNotInstalled_SaysWhichClientCouldNotBeStarted()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var client = DockerClient.Create(new DockerClientOptions
        {
            Endpoint = "ssh://deploy@build-01:2222",
            SshExecutablePath = "codebrix-docker-no-such-ssh-client",
        });

        //Act
        DockerException failure = null;
        try
        {
            await client.System.GetVersionAsync(cancellation.Token);
        }
        catch (DockerException ex)
        {
            failure = ex;
        }

        output.WriteLine($"missing local ssh: {failure?.Message}");

        //Assert
        failure.Should().NotBeNull();
        failure.Message.Should().Contain("Could not start the SSH client 'codebrix-docker-no-such-ssh-client'");
        failure.Message.Should().Contain("must be installed and on PATH");
        failure.Message.Should().Contain("SshExecutablePath");
    }

    [Fact]
    public async Task BuildAsync_WhenTheClientNamesAnEndpoint_RunsTheDockerCliAgainstThatEndpoint()
    {
        //Arrange
        // Image builds and credentialled pulls go through the docker command line, which resolves its own
        // daemon from DOCKER_HOST. Unless the client's endpoint is handed to it, a client built on
        // 'ssh://elsewhere' would quietly build on the local daemon instead. Nothing listens on the
        // endpoint below, so a build that honours it must fail.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", "FROM alpine:3.19\nRUN true\n");

        using var elsewhere = DockerClient.Create(new DockerClientOptions
        {
            Endpoint = "unix:///var/run/codebrix-docker-no-such-daemon.sock",
        });

        //Act
        Func<Task> build = () => elsewhere.Images.BuildAsync(new ImageBuildSpec
        {
            ContextDirectory = context.Path,
            Tags = { DockerTestFixture.ImageRepositoryPrefix + "cli-endpoint:latest" },
        }, cancellation.Token);

        //Assert
        var failure = await build.Should().ThrowAsync<DockerCliException>();
        failure.Which.Message.Should().Contain("codebrix-docker-no-such-daemon.sock");
    }

    [Fact]
    public async Task SshEndpoint_AnswersPingVersionAndContainerList()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var harness = await fixture.GetSshdHarnessAsync(cancellation.Token);
        using var remote = DockerClient.Create(harness.CreateOptions(harness.Endpoint));

        //Act
        var reachable = await remote.System.PingAsync(cancellation.Token);
        var version = await remote.System.GetVersionAsync(cancellation.Token);
        var containers = await remote.Containers.ListAsync(all: true, labelFilters: null, cancellation.Token);
        var local = await Client.System.GetVersionAsync(cancellation.Token);

        //Assert
        remote.Endpoint.Should().Be(harness.Endpoint);
        reachable.Should().BeTrue();
        version.ApiVersion.Should().NotBeNullOrWhiteSpace();

        // The socket inside the container is this very daemon, so the two clients must agree.
        version.Version.Should().Be(local.Version);
        containers.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SshEndpoint_RunsAContainerThroughItsWholeLifecycle()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var harness = await fixture.GetSshdHarnessAsync(cancellation.Token);
        using var remote = DockerClient.Create(harness.CreateOptions(harness.Endpoint));

        var spec = fixture.Spec("ssh-roundtrip", "alpine:latest", "sh", "-c", "echo started; sleep 300");
        string id = null;

        try
        {
            //Act
            id = await remote.Containers.CreateAsync(spec, cancellation.Token);
            await remote.Containers.StartAsync(id, cancellation.Token);

            var inspected = await remote.Containers.InspectAsync(id, cancellation.Token);
            var exec = await remote.Containers.ExecAsync(id, ["sh", "-c", "echo OVER-SSH-$((6*7))"],
                cancellationToken: cancellation.Token);
            var logs = await remote.Containers.GetLogsAsync(id, tail: null, timestamps: false,
                cancellationToken: cancellation.Token);

            await remote.Containers.StopAsync(id, timeoutSeconds: 5, cancellation.Token);
            var stopped = await remote.Containers.InspectAsync(id, cancellation.Token);

            await remote.Containers.RemoveAsync(id, force: true, removeVolumes: false, cancellation.Token);

            // The daemon on the far end is the local one, so the local client must agree it is gone.
            var survivors = await Client.Containers.ListAsync(all: true, DockerTestFixture.TestLabelFilter,
                cancellation.Token);

            //Assert
            inspected.State.Running.Should().BeTrue();
            exec.ExitCode.Should().Be(0);
            exec.Stdout.Should().Contain("OVER-SSH-42");
            logs.Stdout.Should().Contain("started");
            stopped.State.Running.Should().BeFalse();
            survivors.Should().NotContain(container => container.Id == id);

            id = null;
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecStreamAsync_OverSsh_DeliversStandardInputAndSignalsEndOfFile()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var harness = await fixture.GetSshdHarnessAsync(cancellation.Token);
        using var remote = DockerClient.Create(harness.CreateOptions(harness.Endpoint));

        var spec = fixture.Spec("ssh-exec", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await remote.Containers.RunAsync(spec, cancellation.Token);

            //Act
            await using var session = await remote.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh", "-c", "cat; echo EOF-SEEN"],
                AttachStdin = true,
                Tty = false,
            }, cancellation.Token);

            await session.WriteAsync("through-the-tunnel\n", cancellation.Token);
            await session.CloseStandardInputAsync(cancellation.Token);

            var logs = await session.ReadToEndAsync(cancellation.Token);
            var exitCode = await session.WaitForExitAsync(cancellation.Token);

            //Assert
            // Closing the ssh client's standard input reaches the container: OpenSSH forwards end of file
            // to the remote command, and dial-stdio shuts down the writing half of the daemon socket.
            session.CanCloseStandardInput.Should().BeTrue();
            logs.Stdout.Should().Be("through-the-tunnel\nEOF-SEEN\n");
            exitCode.Should().Be(0);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task GetVersionAsync_WhenTheHostKeyIsNotKnown_RefusesAndSaysToConnectByHandFirst()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var harness = await fixture.GetSshdHarnessAsync(cancellation.Token);
        using var untrusting = DockerClient.Create(
            harness.CreateOptions(harness.Endpoint, harness.EmptyKnownHostsPath));

        //Act
        DockerException failure = null;
        try
        {
            await untrusting.System.GetVersionAsync(cancellation.Token);
        }
        catch (DockerException ex)
        {
            failure = ex;
        }

        var reachable = await untrusting.System.PingAsync(cancellation.Token);

        output.WriteLine($"unknown host key: {failure?.Message}");

        //Assert
        failure.Should().NotBeNull();
        failure.Message.Should().Contain("host key");
        failure.Message.Should().Contain("never accepts a host key automatically");
        failure.Message.Should().Contain("Connect once by hand");
        failure.Message.Should().Contain("known_hosts");
        failure.Message.Should().Contain("Host key verification failed");

        // Ping reports unreachable rather than throwing, exactly as it does for a daemon that is down.
        reachable.Should().BeFalse();
    }

    [Fact]
    public async Task GetVersionAsync_WhenTheRemoteHasNoDockerCli_SaysSo()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var harness = await fixture.GetSshdHarnessAsync(cancellation.Token);
        using var remote = DockerClient.Create(harness.CreateOptions(harness.EndpointWithoutDockerCli));

        //Act
        DockerException failure = null;
        try
        {
            await remote.System.GetVersionAsync(cancellation.Token);
        }
        catch (DockerException ex)
        {
            failure = ex;
        }

        output.WriteLine($"remote without the Docker CLI: {failure?.Message}");

        //Assert
        // The SSH session itself succeeds here; it is the remote command that is missing, and saying so
        // is the difference between a fixable message and a connection that simply stopped answering.
        failure.Should().NotBeNull();
        failure.Message.Should().Contain("has no 'docker' command on its PATH");
        failure.Message.Should().Contain("docker system dial-stdio");
        failure.Message.Should().Contain("needs the Docker CLI installed on the remote host");
    }
}
