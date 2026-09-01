// Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// The raw connection left behind after the daemon answers a hijack request with
/// <c>101 UPGRADED</c>: from that point the socket carries the container's standard streams instead
/// of HTTP.
/// </summary>
/// <remarks>
/// The stream replays any bytes that arrived in the same read as the response headers before it
/// touches the socket again, so no output is lost. On a Unix domain socket or a TCP connection the
/// writing half can be shut down on its own (<see cref="CanCloseWrite"/>); a Windows named pipe has
/// no equivalent, and there the whole stream must be disposed to signal end of input.
/// </remarks>
internal sealed class HijackedStream : Stream, IWriteClosableStream
{
    /// <summary>The content type the daemon uses for a TTY exec: verbatim pseudo-terminal bytes.</summary>
    public const string RawStreamContentType = "application/vnd.docker.raw-stream";

    /// <summary>The content type the daemon uses for a non-TTY exec: <c>stdcopy</c> frames.</summary>
    public const string MultiplexedStreamContentType = "application/vnd.docker.multiplexed-stream";

    private readonly Stream _inner;
    private readonly Socket _socket;
    private readonly IWriteClosableStream _writeClosableInner;
    private readonly byte[] _prefix;
    private int _prefixOffset;
    private readonly int _prefixEnd;
    private bool _writeClosed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HijackedStream"/> class.
    /// </summary>
    /// <param name="inner">The connected transport stream, whose lifetime this instance takes over.</param>
    /// <param name="contentType">The <c>Content-Type</c> the daemon answered the hijack with.</param>
    /// <param name="prefix">A buffer holding bytes already read past the response headers.</param>
    /// <param name="prefixOffset">The first unconsumed index in <paramref name="prefix"/>.</param>
    /// <param name="prefixEnd">The index one past the last valid byte in <paramref name="prefix"/>.</param>
    public HijackedStream(Stream inner, string contentType, byte[] prefix, int prefixOffset, int prefixEnd)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ContentType = contentType ?? string.Empty;
        _prefix = prefix ?? [];
        _prefixOffset = prefixOffset;
        _prefixEnd = prefixEnd;
        _socket = inner is NetworkStream network ? network.Socket : null;

        // The ssh:// transport is not a socket, but it closes the far end's standard input for us.
        _writeClosableInner = inner as IWriteClosableStream;
    }

    /// <summary>Gets the <c>Content-Type</c> the daemon answered the hijack with.</summary>
    public string ContentType { get; }

    /// <summary>
    /// Gets a value indicating whether the daemon declared the connection to carry verbatim
    /// pseudo-terminal bytes rather than <c>stdcopy</c> frames.
    /// </summary>
    public bool IsRawStream =>
        ContentType.StartsWith(RawStreamContentType, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool CanCloseWrite => _socket is not null || (_writeClosableInner?.CanCloseWrite ?? false);

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

        if (!CanCloseWrite)
        {
            throw new NotSupportedException(
                "This Docker transport cannot close standard input without closing the whole connection. " +
                "Check CanCloseWrite first, and dispose the stream instead.");
        }

        _writeClosed = true;

        if (_socket is null)
        {
            await _writeClosableInner.CloseWriteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            _socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            // The peer has already gone; the process inside the container will see end of input anyway.
        }
        catch (ObjectDisposedException)
        {
            // Same reasoning: nothing is left to shut down.
        }
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var replayed = TakePrefix(buffer);
        return replayed > 0 ? replayed : _inner.Read(buffer);
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var replayed = TakePrefix(buffer.Span);
        return replayed > 0
            ? ValueTask.FromResult(replayed)
            : _inner.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfWriteClosed();
        _inner.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfWriteClosed();
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Copies whatever is left of the bytes that arrived alongside the response headers.
    /// </summary>
    /// <param name="buffer">The caller's buffer.</param>
    /// <returns>The number of bytes replayed, which is zero once the prefix is exhausted.</returns>
    private int TakePrefix(Span<byte> buffer)
    {
        var available = _prefixEnd - _prefixOffset;
        if (available <= 0 || buffer.Length == 0)
        {
            return 0;
        }

        var count = Math.Min(available, buffer.Length);
        _prefix.AsSpan(_prefixOffset, count).CopyTo(buffer);
        _prefixOffset += count;
        return count;
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
}
