// Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.
using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Decodes Docker's <c>stdcopy</c> framing, the protocol used by container logs and exec output when
/// the container has no TTY.
/// </summary>
/// <remarks>
/// Each frame is an 8-byte header followed by its payload. Byte 0 identifies the stream
/// (0 stdin, 1 stdout, 2 stderr, 3 system error); bytes 1-3 are zero; bytes 4-7 are the payload
/// length as a big-endian 32-bit integer. When a container was created with a TTY the daemon sends
/// the raw output instead, so this reader sniffs the buffer first and falls back to treating
/// everything as standard output.
/// </remarks>
internal static class MultiplexedStreamReader
{
    private const int HeaderLength = 8;

    /// <summary>Stream identifiers used by the stdcopy header.</summary>
    private const byte StdIn = 0;
    private const byte StdOut = 1;
    private const byte StdErr = 2;
    private const byte SystemErr = 3;

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
