using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker.Tests;

/// <summary>Waits for a live daemon to reach a state instead of sleeping for a fixed period.</summary>
internal static class Poll
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Repeats <paramref name="probe"/> until <paramref name="isSatisfied"/> accepts its result.</summary>
    public static async Task<T> UntilAsync<T>(Func<CancellationToken, Task<T>> probe, Func<T, bool> isSatisfied,
        TimeSpan timeout, string description, TimeSpan? interval = null,
        CancellationToken cancellationToken = default)
    {
        var pause = interval ?? DefaultInterval;
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            var value = await probe(cancellationToken);
            if (isSatisfied(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:0}s waiting for {description}. Last value: {value}.");
            }

            await Task.Delay(pause, cancellationToken);
        }
    }

    /// <summary>Repeats <paramref name="probe"/> until it returns <see langword="true"/>.</summary>
    public static Task UntilTrueAsync(Func<CancellationToken, Task<bool>> probe, TimeSpan timeout,
        string description, TimeSpan? interval = null, CancellationToken cancellationToken = default) =>
        UntilAsync(probe, satisfied => satisfied, timeout, description, interval, cancellationToken);
}

/// <summary>
/// A synchronous progress sink. <see cref="Progress{T}"/> marshals its callbacks asynchronously, which
/// loses reports when the observed operation finishes before the callbacks drain.
/// </summary>
internal sealed class CollectingProgress : IProgress<string>
{
    private readonly List<string> _lines = [];

    /// <summary>Gets a snapshot of everything reported so far.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Report(string value)
    {
        lock (_lines)
        {
            _lines.Add(value);
        }
    }
}

/// <summary>A throwaway directory used as a build context or to hold a Dockerfile.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"{DockerTestFixture.NamePrefix}ctx-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the directory path.</summary>
    public string Path { get; }

    /// <summary>Writes a file into the directory and returns its full path.</summary>
    public string WriteFile(string name, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, name);
        File.WriteAllText(fullPath, content.ReplaceLineEndings("\n"));
        return fullPath;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception)
        {
            // A temporary directory that cannot be deleted is not worth failing a test over.
        }
    }
}

/// <summary>
/// A read-only stream that cancels a token source once a given number of bytes has been read from it,
/// so that a test can cancel an operation part way through a transfer without relying on timing.
/// </summary>
internal sealed class CancelAfterBytesStream(Stream inner, long threshold, CancellationTokenSource source) : Stream
{
    private long _read;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, 64 * 1024)]);
        Advance(read);
        return read;
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, 64 * 1024)], cancellationToken);
        Advance(read);
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Advance(int read)
    {
        _read += read;
        if (_read >= threshold && !source.IsCancellationRequested)
        {
            source.Cancel();
        }
    }
}
