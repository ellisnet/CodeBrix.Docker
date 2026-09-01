using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// A live command running inside a container, with its standard streams attached: read its output,
/// write to its standard input, resize its terminal, and collect its exit code.
/// </summary>
/// <remarks>
/// <para>
/// Obtained from <see cref="ContainerOperations.ExecStreamAsync"/>. The session owns a connection to
/// the daemon that was upgraded away from HTTP, so it must be disposed; disposing it while the
/// command is still running drops the connection and the command sees its terminal go away.
/// </para>
/// <para>
/// The exit code never arrives on the stream. Read output until <see cref="ExecStreamReadResult.EndOfStream"/>,
/// then call <see cref="WaitForExitAsync"/> or <see cref="InspectAsync"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var spec = new ExecSpec { Command = ["/bin/sh"], AttachStdin = true, Tty = true };
/// await using var shell = await client.Containers.ExecStreamAsync(containerId, spec);
///
/// await shell.WriteLineAsync("echo hello");
/// await shell.WriteLineAsync("exit 0");
///
/// var buffer = new byte[4096];
/// while (true)
/// {
///     var read = await shell.ReadAsync(buffer);
///     if (read.EndOfStream) { break; }
///     Console.Write(Encoding.UTF8.GetString(buffer, 0, read.Count));
/// }
///
/// var exitCode = await shell.WaitForExitAsync();
/// </code>
/// </example>
public sealed class ContainerExecStream : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly DockerApiClient _api;
    private readonly MultiplexedStreamReader _reader;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerExecStream"/> class.
    /// </summary>
    /// <param name="api">The client the session issues its resize and inspect calls on.</param>
    /// <param name="execId">The exec instance's id.</param>
    /// <param name="transport">The upgraded connection, whose lifetime this instance takes over.</param>
    /// <param name="tty">Whether a pseudo-terminal was requested.</param>
    internal ContainerExecStream(DockerApiClient api, string execId, HijackedStream transport, bool tty)
    {
        _api = api;
        ExecId = execId;
        IsTty = tty;

        // The daemon states the framing in the hijack response's Content-Type; trust that over the
        // requested Tty flag, which only says what was asked for.
        UsesRawFraming = transport.IsRawStream;
        _reader = new MultiplexedStreamReader(transport, UsesRawFraming);
    }

    /// <summary>Gets the exec instance's id, as used by <c>GET /exec/{id}/json</c>.</summary>
    public string ExecId { get; }

    /// <summary>Gets a value indicating whether a pseudo-terminal was requested for this session.</summary>
    public bool IsTty { get; }

    /// <summary>
    /// Gets a value indicating whether the daemon is sending verbatim terminal bytes rather than
    /// <c>stdcopy</c> frames. Raw sessions report every chunk as
    /// <see cref="ExecStreamTarget.StandardOutput"/>, because a terminal has only one stream.
    /// </summary>
    public bool UsesRawFraming { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="CloseStandardInputAsync"/> is available on this
    /// transport. Unix domain sockets and TCP connections support it; Windows named pipes do not.
    /// </summary>
    public bool CanCloseStandardInput => _reader.CanCloseWrite;

    /// <summary>
    /// Reads the next chunk of output, waiting until some arrives or the command's streams close.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// How many bytes were read and which stream they came from. A result whose
    /// <see cref="ExecStreamReadResult.EndOfStream"/> is <see langword="true"/> means the command's
    /// streams have closed; the exit code is then available from <see cref="WaitForExitAsync"/>.
    /// </returns>
    /// <exception cref="DockerException">
    /// The daemon reported a failure of its own on its fourth stream, or ended the stream part-way
    /// through a frame. A command that could not be started is not reported this way: the runtime's
    /// message arrives as ordinary output and the exit code is 127.
    /// </exception>
    public Task<ExecStreamReadResult> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _reader.ReadAsync(buffer, cancellationToken);

    /// <summary>
    /// Reads the rest of the session's output, waiting for the command's streams to close.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The two decoded streams. A raw session has no separate standard error, so everything lands in
    /// <see cref="ContainerLogs.Stdout"/>.
    /// </returns>
    /// <exception cref="DockerException">The daemon reported a failure of its own.</exception>
    public Task<ContainerLogs> ReadToEndAsync(CancellationToken cancellationToken = default) =>
        _reader.ReadRemainingAsync(cancellationToken);

    /// <summary>
    /// Writes to the command's standard input. Requires <see cref="ExecSpec.AttachStdin"/>.
    /// </summary>
    /// <param name="buffer">The bytes to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the bytes have been flushed to the daemon.</returns>
    public Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _reader.WriteAsync(buffer, cancellationToken);

    /// <summary>
    /// Writes UTF-8 text to the command's standard input, with no line ending added.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the text has been flushed to the daemon.</returns>
    public Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _reader.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    /// <summary>
    /// Writes UTF-8 text followed by a line feed, which is what a shell reads as one typed line.
    /// </summary>
    /// <param name="text">The line to send, without its terminator.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the line has been flushed to the daemon.</returns>
    public Task WriteLineAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _reader.WriteAsync(Encoding.UTF8.GetBytes(text + "\n"), cancellationToken);
    }

    /// <summary>
    /// Closes the command's standard input while leaving its output flowing, which is how a command
    /// reading from a pipe is told that its input has ended.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the shutdown has been requested.</returns>
    /// <exception cref="NotSupportedException">
    /// The transport cannot close one half on its own; check <see cref="CanCloseStandardInput"/>
    /// first, and dispose the session instead.
    /// </exception>
    public Task CloseStandardInputAsync(CancellationToken cancellationToken = default) =>
        _reader.CloseWriteAsync(cancellationToken);

    /// <summary>
    /// Tells the daemon the terminal has been resized, so that programs inside the container lay
    /// their output out to the new size.
    /// </summary>
    /// <param name="height">The new height in rows. Must be greater than zero.</param>
    /// <param name="width">The new width in columns. Must be greater than zero.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the daemon has accepted the new size.</returns>
    /// <remarks>Only a session started with <see cref="ExecSpec.Tty"/> has a terminal to resize.</remarks>
    public Task ResizeAsync(int height, int width, CancellationToken cancellationToken = default) =>
        ContainerOperations.ResizeExecCoreAsync(_api, ExecId, height, width, cancellationToken);

    /// <summary>
    /// Reads the exec instance's current state, including its exit code once it has finished.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exec instance's state.</returns>
    public Task<ExecInspectResult> InspectAsync(CancellationToken cancellationToken = default) =>
        ContainerOperations.InspectExecCoreAsync(_api, ExecId, cancellationToken);

    /// <summary>
    /// Waits for the command to finish and returns its exit code.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The command's exit code.</returns>
    /// <remarks>
    /// Read the session's output to the end first. A command whose output nobody is reading blocks
    /// once the daemon's buffer fills, and this call would then wait forever.
    /// </remarks>
    public async Task<long> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var inspect = await InspectAsync(cancellationToken).ConfigureAwait(false);
            if (!inspect.Running)
            {
                return inspect.ExitCode ?? 0;
            }

            await Task.Delay(ExitPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _reader.DisposeAsync().ConfigureAwait(false);
    }
}
