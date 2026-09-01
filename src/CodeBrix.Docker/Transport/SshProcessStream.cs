using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// The daemon connection carried by a child <c>ssh</c> process running <c>docker system dial-stdio</c>
/// on the remote host: writes go to the child's standard input, reads come from its standard output.
/// </summary>
/// <remarks>
/// <para>
/// The process lifetime is the stream lifetime — disposing the stream kills the child — and the child's
/// standard error is captured throughout, so a handshake that fails (an untrusted host key, a refused
/// key, a remote host with no <c>docker</c> command) surfaces as a <see cref="DockerException"/> that
/// says what went wrong instead of an empty pipe.
/// </para>
/// <para>
/// Closing the writing half closes the child's standard input, which OpenSSH forwards to the remote
/// command as end of file; <c>docker system dial-stdio</c> in turn shuts down the writing half of the
/// remote socket. Interactive <c>exec</c> therefore signals end of input over <c>ssh://</c> exactly as
/// it does over a local socket.
/// </para>
/// </remarks>
internal sealed class SshProcessStream : Stream, IWriteClosableStream
{
    private const int MaxCapturedErrorLength = 65536;

    private static readonly TimeSpan ExitDiagnosisTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly DockerEndpoint _endpoint;
    private readonly string _executablePath;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly StringBuilder _errorText = new();
    private readonly Task _errorPump;
    private bool _writeClosed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SshProcessStream"/> class around a started process.
    /// </summary>
    /// <param name="process">The running SSH client, whose lifetime this instance takes over.</param>
    /// <param name="endpoint">The endpoint the process was started for, used in error messages.</param>
    /// <param name="executablePath">The SSH client that was started, used in error messages.</param>
    public SshProcessStream(Process process, DockerEndpoint endpoint, string executablePath)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _executablePath = executablePath ?? string.Empty;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _errorPump = Task.Run(PumpStandardErrorAsync);
    }

    /// <inheritdoc />
    public bool CanCloseWrite => true;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_writeClosed;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public async Task CloseWriteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_writeClosed)
        {
            return;
        }

        _writeClosed = true;
        try
        {
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The child has already gone; closing standard input is moot.
        }

        try
        {
            await _input.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Same reasoning: the remote command has seen end of input either way.
        }
    }

    /// <inheritdoc />
    public override void Flush() => _input.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _input.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return _output.Read(buffer, offset, count);
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _output.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read;
        try
        {
            read = await _output.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPipeFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw await DescribeFailureAsync(ex).ConfigureAwait(false);
        }

        if (read == 0)
        {
            // End of the child's output means the SSH session is over. When it ended badly, say so here
            // rather than letting the caller see a connection that simply stopped answering.
            var failure = await TryDescribeFailureAsync().ConfigureAwait(false);
            if (failure is not null)
            {
                throw failure;
            }
        }

        return read;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfWriteClosed();
        _input.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfWriteClosed();
        _input.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfWriteClosed();

        try
        {
            await _input.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPipeFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw await DescribeFailureAsync(ex).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            Terminate();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            Terminate();
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Gets whatever the SSH client has written to standard error so far.</summary>
    private string CapturedErrorText
    {
        get
        {
            lock (_errorText)
            {
                return _errorText.ToString();
            }
        }
    }

    /// <summary>Kills the SSH client and releases its pipes.</summary>
    private void Terminate()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            // Already gone, or beyond our reach; the pipes below are closed regardless.
        }

        SafeDispose(_input);
        SafeDispose(_output);
        _process.Dispose();

        static void SafeDispose(Stream stream)
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The child closed the pipe first; nothing to release.
            }
        }
    }

    /// <summary>
    /// Turns a pipe failure into the most specific exception available, falling back to reporting the
    /// underlying I/O error when the child is still alive.
    /// </summary>
    private async ValueTask<Exception> DescribeFailureAsync(Exception underlying)
    {
        var failure = await TryDescribeFailureAsync().ConfigureAwait(false);
        return failure ?? new DockerException(
            $"The ssh:// connection to {SshDialStdioConnection.Describe(_endpoint)} failed: {underlying.Message}",
            underlying);
    }

    /// <summary>
    /// Waits briefly for the SSH client to exit and, if it exited badly, builds the exception that says
    /// why. Returns <see langword="null"/> when the session simply ended.
    /// </summary>
    private async ValueTask<DockerException> TryDescribeFailureAsync()
    {
        if (_disposed)
        {
            // We killed the child ourselves; that is not a failure to report.
            return null;
        }

        try
        {
            using var deadline = new CancellationTokenSource(ExitDiagnosisTimeout);
            await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            // Still running, or no longer inspectable: nothing to diagnose.
            return null;
        }

        // Let the last of standard error arrive, but never wait on it indefinitely.
        await Task.WhenAny(_errorPump, Task.Delay(ExitDiagnosisTimeout)).ConfigureAwait(false);

        int exitCode;
        try
        {
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return exitCode == 0
            ? null
            : SshDialStdioConnection.CreateFailure(_endpoint, _executablePath, exitCode, CapturedErrorText);
    }

    /// <summary>Collects the SSH client's standard error for as long as the process lives.</summary>
    private async Task PumpStandardErrorAsync()
    {
        var buffer = new char[1024];

        try
        {
            while (true)
            {
                var read = await _process.StandardError.ReadAsync(buffer, CancellationToken.None)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                lock (_errorText)
                {
                    if (_errorText.Length < MaxCapturedErrorLength)
                    {
                        _errorText.Append(buffer, 0, read);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe went away with the process; whatever was captured is what we have.
        }
    }

    private void ThrowIfWriteClosed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writeClosed)
        {
            throw new InvalidOperationException(
                "Standard input has already been closed on this Docker exec stream.");
        }
    }

    private static bool IsPipeFailure(Exception exception) =>
        exception is IOException or ObjectDisposedException or InvalidOperationException;
}
