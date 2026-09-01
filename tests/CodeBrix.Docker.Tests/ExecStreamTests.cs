using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Exercises the streaming exec API against a live daemon: both framings, standard input and its
/// half-close, terminal resizing, exit-code propagation, cancellation, and the image that ships no
/// such shell.
/// </summary>
[Collection(DockerTestCollection.Name)]
public sealed class ExecStreamTests(DockerTestFixture fixture)
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task ExecStreamAsync_WithTty_UsesRawFramingAndEchoesWhatIsTyped()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-tty", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            //Act
            await using var shell = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh"],
                AttachStdin = true,
                Tty = true,
                ConsoleHeight = 24,
                ConsoleWidth = 80,
            }, cancellation.Token);

            await shell.WriteLineAsync("echo MARKER-$((6*7))", cancellation.Token);
            await shell.WriteLineAsync("exit 7", cancellation.Token);

            var logs = await shell.ReadToEndAsync(cancellation.Token);
            var exitCode = await shell.WaitForExitAsync(cancellation.Token);

            //Assert
            shell.IsTty.Should().BeTrue();
            shell.UsesRawFraming.Should().BeTrue();
            logs.Stdout.Should().Contain("MARKER-42");

            // A pseudo-terminal echoes what is typed and ends its lines with CRLF; a pipe does neither.
            logs.Stdout.Should().Contain("echo MARKER-$((6*7))");
            logs.Stdout.Should().Contain("\r\n");

            // Raw framing merges the two output streams, so nothing can arrive on standard error.
            logs.Stderr.Should().BeEmpty();
            exitCode.Should().Be(7);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecStreamAsync_WithoutTty_KeepsStandardOutputAndStandardErrorApart()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-multiplexed", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            //Act
            await using var session = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh", "-c", "echo to-stdout; echo to-stderr 1>&2; exit 3"],
                Tty = false,
            }, cancellation.Token);

            var logs = await session.ReadToEndAsync(cancellation.Token);
            var exitCode = await session.WaitForExitAsync(cancellation.Token);

            //Assert
            session.UsesRawFraming.Should().BeFalse();
            logs.Stdout.Should().Be("to-stdout\n");
            logs.Stderr.Should().Be("to-stderr\n");
            exitCode.Should().Be(3);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecStreamAsync_WithStandardInput_DeliversItAndThenSignalsEndOfFile()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-stdin", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            //Act
            await using var session = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh", "-c", "cat; echo EOF-SEEN"],
                AttachStdin = true,
                Tty = false,
            }, cancellation.Token);

            await session.WriteAsync("line-one\nline-two\n", cancellation.Token);
            await session.CloseStandardInputAsync(cancellation.Token);

            var logs = await session.ReadToEndAsync(cancellation.Token);
            var exitCode = await session.WaitForExitAsync(cancellation.Token);

            //Assert
            session.CanCloseStandardInput.Should().BeTrue();
            logs.Stdout.Should().Be("line-one\nline-two\nEOF-SEEN\n");
            exitCode.Should().Be(0);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ResizeExecAsync_ChangesTheTerminalSizeTheContainerSees()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-resize", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await using var shell = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh"],
                AttachStdin = true,
                Tty = true,
                ConsoleHeight = 24,
                ConsoleWidth = 80,
            }, cancellation.Token);

            //Act
            await shell.WriteLineAsync("stty size", cancellation.Token);
            var beforeResize = await ReadUntilAsync(shell, "24 80", cancellation.Token);

            await Client.Containers.ResizeExecAsync(shell.ExecId, height: 40, width: 120, cancellation.Token);

            await shell.WriteLineAsync("stty size", cancellation.Token);
            var afterResize = await ReadUntilAsync(shell, "40 120", cancellation.Token);

            await shell.WriteLineAsync("exit 0", cancellation.Token);
            await shell.ReadToEndAsync(cancellation.Token);
            var exitCode = await shell.WaitForExitAsync(cancellation.Token);

            //Assert
            beforeResize.Should().Contain("24 80");
            afterResize.Should().Contain("40 120");
            exitCode.Should().Be(0);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecStreamAsync_ForAShellTheImageDoesNotShip_ReportsTheRuntimeMessageAndExitCode127()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-no-shell", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            //Act
            // alpine ships /bin/sh and /bin/ash but no bash, so this asks for a shell that is not there.
            await using var session = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/bash"],
                AttachStdin = true,
                Tty = true,
            }, cancellation.Token);

            var logs = await session.ReadToEndAsync(cancellation.Token);
            var inspect = await session.InspectAsync(cancellation.Token);
            var exitCode = await session.WaitForExitAsync(cancellation.Token);

            //Assert
            // The stream must end on its own rather than leaving the caller waiting for a prompt.
            logs.Combined.Should().Contain("/bin/bash");
            logs.Combined.Should().Contain("no such file or directory");
            inspect.Running.Should().BeFalse();
            exitCode.Should().Be(127);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ReadAsync_WhenItsTokenIsCancelled_StopsWaitingForMoreOutput()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var spec = fixture.Spec("exec-cancel", "alpine:latest", "sleep", "300");
        string id = null;

        try
        {
            id = await Client.Containers.RunAsync(spec, cancellation.Token);

            await using var shell = await Client.Containers.ExecStreamAsync(id, new ExecSpec
            {
                Command = ["/bin/sh"],
                AttachStdin = true,
                Tty = true,
            }, cancellation.Token);

            var buffer = new byte[4096];

            // Drain the prompt, so that the next read has nothing to return and really does wait.
            await shell.ReadAsync(buffer, cancellation.Token);

            //Act
            using var blocked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            blocked.CancelAfter(TimeSpan.FromMilliseconds(500));
            Func<Task> waitingRead = () => shell.ReadAsync(buffer, blocked.Token);

            using var alreadyCancelled = new CancellationTokenSource();
            await alreadyCancelled.CancelAsync();
            Func<Task> immediateRead = () => shell.ReadAsync(buffer, alreadyCancelled.Token);

            //Assert
            await waitingRead.Should().ThrowAsync<OperationCanceledException>();
            await immediateRead.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(id);
        }
    }

    [Fact]
    public async Task ExecStreamAsync_WithAnIncompleteSpec_ThrowsBeforeTouchingTheDaemon()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var noCommand = new ExecSpec();
        var detached = new ExecSpec
        {
            Command = ["/bin/sh"],
            AttachStdin = false,
            AttachStdout = false,
            AttachStderr = false,
        };

        //Act
        Func<Task> withoutSpec = () => Client.Containers.ExecStreamAsync("missing", null, cancellation.Token);
        Func<Task> withoutCommand = () => Client.Containers.ExecStreamAsync("missing", noCommand, cancellation.Token);
        Func<Task> withoutStreams = () => Client.Containers.ExecStreamAsync("missing", detached, cancellation.Token);

        //Assert
        await withoutSpec.Should().ThrowAsync<ArgumentNullException>();
        await withoutCommand.Should().ThrowAsync<ArgumentException>();
        await withoutStreams.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResizeExecAsync_WithDimensionsThatAreNotPositive_ThrowsArgumentOutOfRange()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        //Act
        Func<Task> zeroHeight = () => Client.Containers.ResizeExecAsync("exec-id", 0, 80, cancellation.Token);
        Func<Task> zeroWidth = () => Client.Containers.ResizeExecAsync("exec-id", 24, 0, cancellation.Token);
        Func<Task> noId = () => Client.Containers.ResizeExecAsync(" ", 24, 80, cancellation.Token);

        //Assert
        await zeroHeight.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await zeroWidth.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await noId.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Reads from a session until its accumulated output contains <paramref name="marker"/>.
    /// </summary>
    private static async Task<string> ReadUntilAsync(ContainerExecStream stream, string marker,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadTimeout);

        var text = new StringBuilder();
        var buffer = new byte[4096];

        while (!text.ToString().Contains(marker, StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, deadline.Token);
            if (read.EndOfStream)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
        }

        return text.ToString();
    }
}
