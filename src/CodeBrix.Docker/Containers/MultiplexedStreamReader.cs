// Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Decodes the two framings the Docker daemon uses on container logs, attach and exec streams, one
/// chunk at a time, and writes back on the same connection when it is bidirectional.
/// </summary>
/// <remarks>
/// <para>
/// Without a TTY the daemon uses its <c>stdcopy</c> framing: an 8-byte header followed by its
/// payload, where byte 0 identifies the stream (0 stdin, 1 stdout, 2 stderr, 3 system error), bytes
/// 1-3 are zero, and bytes 4-7 are the payload length as a big-endian 32-bit integer. With a TTY the
/// daemon allocates a pseudo-terminal inside the container and sends its bytes verbatim, with no
/// framing at all and the two output streams already merged.
/// </para>
/// <para>
/// An instance is told which framing to expect — the daemon announces it in the hijack response's
/// <c>Content-Type</c> — while the static <see cref="Demultiplex"/> path, used by the buffered logs
/// and one-shot exec calls, sniffs the buffer instead.
/// </para>
/// </remarks>
internal sealed class MultiplexedStreamReader : IDisposable, IAsyncDisposable
{
    private const int HeaderLength = 8;
    private const int CopyBufferSize = 16384;

    /// <summary>Stream identifiers used by the stdcopy header.</summary>
    private const byte StdIn = 0;
    private const byte StdOut = 1;
    private const byte StdErr = 2;
    private const byte SystemErr = 3;

    private readonly Stream _stream;
    private readonly byte[] _header = new byte[HeaderLength];
    private ExecStreamTarget _target = ExecStreamTarget.StandardOutput;
    private bool _targetIsSystemError;
    private int _remaining;
    private MemoryStream _systemErrorMessage;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiplexedStreamReader"/> class over a live
    /// daemon stream.
    /// </summary>
    /// <param name="stream">The stream, whose lifetime this instance takes over.</param>
    /// <param name="raw">
    /// <see langword="true"/> when the daemon is sending verbatim pseudo-terminal bytes;
    /// <see langword="false"/> when it is sending <c>stdcopy</c> frames.
    /// </param>
    public MultiplexedStreamReader(Stream stream, bool raw)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        IsRaw = raw;
    }

    /// <summary>Gets a value indicating whether the stream carries verbatim bytes rather than frames.</summary>
    public bool IsRaw { get; }

    /// <summary>Gets a value indicating whether standard input can be closed on its own.</summary>
    public bool CanCloseWrite => _stream is IWriteClosableStream { CanCloseWrite: true };

    /// <summary>
    /// Reads the next chunk of output, blocking until some arrives or the stream ends.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>How many bytes were read and which stream they came from.</returns>
    /// <exception cref="DockerException">
    /// The daemon reported a failure of its own, or ended the stream part-way through a frame.
    /// </exception>
    public async Task<ExecStreamReadResult> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRaw)
        {
            var raw = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            return new ExecStreamReadResult(
                raw == 0 ? ExecStreamTarget.None : ExecStreamTarget.StandardOutput, raw);
        }

        while (true)
        {
            while (_remaining == 0)
            {
                if (!await ReadFrameHeaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    return default;
                }
            }

            var wanted = Math.Min(buffer.Length, _remaining);
            if (wanted == 0)
            {
                // A zero-length caller buffer must not be mistaken for the end of the stream.
                return new ExecStreamReadResult(_target, 0);
            }

            var count = await _stream.ReadAsync(buffer[..wanted], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new DockerException(
                    "The Docker daemon closed the stream part-way through a frame; " +
                    $"{_remaining} more bytes were expected.");
            }

            _remaining -= count;

            if (!_targetIsSystemError)
            {
                return new ExecStreamReadResult(_target, count);
            }

            // The daemon reports its own failures on a fourth stream. Collect the whole message and
            // raise it rather than handing the caller output it cannot tell apart from the command's.
            _systemErrorMessage ??= new MemoryStream();
            _systemErrorMessage.Write(buffer.Span[..count]);

            if (_remaining == 0)
            {
                throw new DockerException(Encoding.UTF8
                    .GetString(_systemErrorMessage.GetBuffer(), 0, (int)_systemErrorMessage.Length).Trim());
            }
        }
    }

    /// <summary>
    /// Writes to the command's standard input.
    /// </summary>
    /// <param name="buffer">The bytes to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the bytes have been flushed to the daemon.</returns>
    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes standard input, leaving output flowing.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the shutdown has been requested.</returns>
    /// <exception cref="NotSupportedException">The transport cannot close one half on its own.</exception>
    public Task CloseWriteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _stream is IWriteClosableStream closable
            ? closable.CloseWriteAsync(cancellationToken)
            : throw new NotSupportedException(
                "This Docker transport cannot close standard input without closing the whole connection.");
    }

    /// <summary>
    /// Reads what is left of the stream, splitting it into standard output and standard error.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The two decoded streams.</returns>
    /// <exception cref="DockerException">The daemon reported a system error inside the stream.</exception>
    public async Task<ContainerLogs> ReadRemainingAsync(CancellationToken cancellationToken)
    {
        var stdout = new ArrayBufferWriter<byte>();
        var stderr = new ArrayBufferWriter<byte>();
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            while (true)
            {
                var result = await ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.EndOfStream)
                {
                    break;
                }

                var sink = result.Target == ExecStreamTarget.StandardError ? stderr : stdout;
                sink.Write(buffer.AsSpan(0, result.Count));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new ContainerLogs(
            Encoding.UTF8.GetString(stdout.WrittenSpan),
            Encoding.UTF8.GetString(stderr.WrittenSpan));
    }

    /// <summary>
    /// Reads the next 8-byte frame header.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="false"/> when the stream ended cleanly between frames.</returns>
    private async Task<bool> ReadFrameHeaderAsync(CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < HeaderLength)
        {
            var read = await _stream
                .ReadAsync(_header.AsMemory(filled, HeaderLength - filled), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                if (filled == 0)
                {
                    return false;
                }

                throw new DockerException(
                    "The Docker daemon closed the stream part-way through a frame header.");
            }

            filled += read;
        }

        var identifier = _header[0];
        if (identifier > SystemErr || _header[1] != 0 || _header[2] != 0 || _header[3] != 0)
        {
            throw new DockerException(
                $"The Docker daemon sent a frame with an unrecognized stream identifier ({identifier}).");
        }

        _targetIsSystemError = identifier == SystemErr;
        _target = identifier == StdErr ? ExecStreamTarget.StandardError : ExecStreamTarget.StandardOutput;
        _remaining = ReadPayloadLength(_header, 0);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        _systemErrorMessage?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _systemErrorMessage?.Dispose();
    }

    // -------------------------------------------------------------------------------------------
    // Buffered decoding, used by container logs and the one-shot exec call
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads <paramref name="stream"/> to the end and splits it into standard output and standard error.
    /// </summary>
    /// <param name="stream">The response stream from a logs or exec-start request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The two decoded streams.</returns>
    /// <exception cref="DockerException">The daemon reported a system error inside the stream.</exception>
    public static async Task<ContainerLogs> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Demultiplex(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>
    /// Splits a buffer of stdcopy frames into standard output and standard error. Buffers that are not
    /// stdcopy-framed (TTY containers) are returned unchanged as standard output.
    /// </summary>
    /// <param name="data">The raw bytes.</param>
    /// <returns>The two decoded streams.</returns>
    /// <exception cref="DockerException">The daemon reported a system error inside the stream.</exception>
    public static ContainerLogs Demultiplex(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return new ContainerLogs(string.Empty, string.Empty);
        }

        if (!LooksMultiplexed(data))
        {
            return new ContainerLogs(Encoding.UTF8.GetString(data), string.Empty);
        }

        var stdout = new ArrayBufferWriter<byte>();
        var stderr = new ArrayBufferWriter<byte>();
        var systemError = new ArrayBufferWriter<byte>();

        var offset = 0;
        while (offset + HeaderLength <= data.Length)
        {
            var target = data[offset];
            var length = ReadPayloadLength(data, offset);
            var payloadStart = offset + HeaderLength;
            var available = Math.Min(length, data.Length - payloadStart);
            if (available < 0)
            {
                break;
            }

            var payload = data.Slice(payloadStart, available);
            switch (target)
            {
                case StdOut:
                case StdIn:
                    stdout.Write(payload);
                    break;
                case StdErr:
                    stderr.Write(payload);
                    break;
                case SystemErr:
                    systemError.Write(payload);
                    break;
            }

            offset = payloadStart + length;
        }

        if (systemError.WrittenCount > 0)
        {
            throw new DockerException(Encoding.UTF8.GetString(systemError.WrittenSpan).Trim());
        }

        return new ContainerLogs(
            Encoding.UTF8.GetString(stdout.WrittenSpan),
            Encoding.UTF8.GetString(stderr.WrittenSpan));
    }

    /// <summary>
    /// Walks the buffer as stdcopy frames to decide whether it really is framed.
    /// </summary>
    private static bool LooksMultiplexed(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength)
        {
            return false;
        }

        var offset = 0;
        var frames = 0;

        while (offset + HeaderLength <= data.Length)
        {
            if (data[offset] > SystemErr || data[offset + 1] != 0 || data[offset + 2] != 0 || data[offset + 3] != 0)
            {
                return false;
            }

            var length = ReadPayloadLength(data, offset);
            if (length < 0)
            {
                return false;
            }

            frames++;
            offset += HeaderLength + length;

            if (offset == data.Length)
            {
                return true;
            }
        }

        // A truncated final frame is still a multiplexed stream, as long as earlier frames parsed.
        return frames > 0 && offset > data.Length;
    }

    private static int ReadPayloadLength(ReadOnlySpan<byte> data, int offset) =>
        (data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7];
}
