using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CodeBrix.Docker;

/// <summary>
/// A <see cref="NamedPipeClientStream"/> whose writing half can be closed on its own, so an
/// interactive <c>exec</c> over <c>npipe://</c> can signal end of input without tearing down the
/// whole connection.
/// </summary>
/// <remarks>
/// <para>
/// A Windows named pipe has no <c>shutdown(SHUT_WR)</c>, but it does not need one: the daemon
/// creates its pipe in MESSAGE mode precisely so that a zero-length message can stand in for end of
/// file. That is the convention <c>go-winio</c> — the pipe library on the daemon's side of the
/// connection — implements as <c>CloseWrite()</c>, and it is what the <c>docker</c> CLI relies on
/// for <c>docker exec -i</c> on Windows. The reader takes a zero-byte message as end of stream while
/// the pipe itself stays open and readable, which is the same half-close a Unix socket gets from
/// <c>SHUT_WR</c>.
/// </para>
/// <para>
/// The zero-length message has to be written by hand. <see cref="PipeStream"/> returns early for an
/// empty buffer, so <c>WriteAsync(ReadOnlyMemory&lt;byte&gt;.Empty)</c> sends nothing at all; the
/// signal goes straight to <c>WriteFile</c> instead. Because the handle is opened for overlapped
/// I/O the call carries an <c>OVERLAPPED</c> whose event handle has its low bit set, which tells
/// Windows to signal that event rather than queue a completion packet to the thread pool port the
/// handle is bound to.
/// </para>
/// <para>
/// A pipe that is not in message mode cannot carry the signal. There <see cref="CanCloseWrite"/> is
/// false and callers get the same <see cref="NotSupportedException"/> they always did.
/// </para>
/// </remarks>
internal sealed class NamedPipeDockerStream : Stream, IWriteClosableStream
{
    private const uint PipeTypeMessage = 0x00000004;
    private const int ErrorBrokenPipe = 109;
    private const int ErrorNoData = 232;
    private const int ErrorPipeNotConnected = 233;
    private const int ErrorIoPending = 997;

    private readonly NamedPipeClientStream _pipe;
    private bool _writeClosed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamedPipeDockerStream"/> class.
    /// </summary>
    /// <param name="pipe">The connected pipe, whose lifetime this instance takes over.</param>
    public NamedPipeDockerStream(NamedPipeClientStream pipe)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        CanCloseWrite = IsMessageModePipe(pipe);
    }

    /// <inheritdoc />
    public bool CanCloseWrite { get; }

    /// <inheritdoc />
    public override bool CanRead => _pipe.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => !_writeClosed && _pipe.CanWrite;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task CloseWriteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_writeClosed)
        {
            return Task.CompletedTask;
        }

        if (!CanCloseWrite)
        {
            throw new NotSupportedException(
                "This Docker named pipe is not in message mode, so standard input cannot be closed " +
                "without closing the whole connection. Check CanCloseWrite first, and dispose the " +
                "stream instead.");
        }

        _writeClosed = true;

        // Whatever was written before this point has already been flushed by the caller, and a pipe
        // delivers its messages in order, so the end-of-input marker cannot overtake the input.
        var handle = _pipe.SafePipeHandle;
        return Task.Run(() =>
        {
            // CanCloseWrite is only ever true on Windows; this is the guard that says so out loud, to
            // the platform analyzer as much as to the reader.
            if (OperatingSystem.IsWindows())
            {
                SendZeroLengthMessage(handle);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public override void Flush() => _pipe.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _pipe.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _pipe.Read(buffer, offset, count);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _pipe.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _pipe.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _pipe.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ThrowIfWriteClosed();
        _pipe.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfWriteClosed();
        _pipe.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ThrowIfWriteClosed();
        return _pipe.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfWriteClosed();
        return _pipe.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _pipe.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _pipe.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether the pipe was created in message mode, which is what makes a zero-length
    /// message an end-of-input marker rather than a write of nothing.
    /// </summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <returns><see langword="true"/> when the half-close can be signalled.</returns>
    private static bool IsMessageModePipe(NamedPipeClientStream pipe)
    {
        // NamedPipeClientStream is emulated over Unix domain sockets away from Windows, and
        // PipeStream.TransmissionMode reports the mode this process asked for rather than the one the
        // server created the pipe with, so ask the operating system directly.
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return GetNamedPipeInfo(pipe.SafePipeHandle, out var flags, out _, out _, out _)
                && (flags & PipeTypeMessage) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a zero-length message, which the daemon reads as end of standard input.
    /// </summary>
    /// <param name="handle">The connected pipe handle.</param>
    /// <exception cref="Win32Exception">The write failed for a reason other than the peer leaving.</exception>
    [SupportedOSPlatform("windows")]
    private static void SendZeroLengthMessage(SafePipeHandle handle)
    {
        using var completed = new ManualResetEvent(false);
        var native = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());

        try
        {
            // The low bit of the event handle suppresses the completion packet that would otherwise go
            // to the thread pool I/O port this handle is bound to; the event is signalled instead.
            var overlapped = new NativeOverlapped
            {
                EventHandle = (IntPtr)(completed.SafeWaitHandle.DangerousGetHandle().ToInt64() | 1L),
            };
            Marshal.StructureToPtr(overlapped, native, false);

            if (WriteFile(handle, IntPtr.Zero, 0, IntPtr.Zero, native))
            {
                return;
            }

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                ThrowUnlessPeerHasGone(error);
                return;
            }

            completed.WaitOne();

            if (!GetOverlappedResult(handle, native, out _, true))
            {
                ThrowUnlessPeerHasGone(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(native);
            GC.KeepAlive(completed);
        }
    }

    /// <summary>
    /// Rethrows a write failure unless it only means the far end has already gone, in which case the
    /// process inside the container sees end of input anyway.
    /// </summary>
    /// <param name="error">The Win32 error code.</param>
    private static void ThrowUnlessPeerHasGone(int error)
    {
        if (error is ErrorBrokenPipe or ErrorNoData or ErrorPipeNotConnected)
        {
            return;
        }

        throw new Win32Exception(error, "Could not signal end of standard input on the Docker named pipe.");
    }

    private void ThrowIfWriteClosed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writeClosed)
        {
            throw new InvalidOperationException(
                "Standard input has already been closed on this Docker named pipe.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeInfo(SafePipeHandle hNamedPipe, out uint lpFlags,
        out uint lpOutBufferSize, out uint lpInBufferSize, out uint lpMaxInstances);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafePipeHandle hFile, IntPtr lpOverlapped,
        out int lpNumberOfBytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafePipeHandle hFile, IntPtr lpBuffer, int nNumberOfBytesToWrite,
        IntPtr lpNumberOfBytesWritten, IntPtr lpOverlapped);
}
