using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>
/// Wraps one <c>ContainerExecStream</c> and passes bytes through untouched. Nothing here decodes,
/// because a read can split a multi-byte UTF-8 sequence and the terminal control accepts bytes.
/// </summary>
internal sealed class ExecSession : IExecSession
{
    private readonly ContainerExecStream _stream;
    private readonly ContainerOperations _containers;
    private int _disposed;

    internal ExecSession(ContainerOperations containers, ContainerExecStream stream,
        string containerId, string shellPath)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ContainerId = containerId;
        ShellPath = shellPath;
        IsRunning = true;
    }

    /// <inheritdoc />
    public string ContainerId { get; }

    /// <inheritdoc />
    public string ExecId => _stream.ExecId;

    /// <inheritdoc />
    public string ShellPath { get; }

    /// <inheritdoc />
    public bool IsTty => _stream.IsTty;

    /// <inheritdoc />
    public bool UsesRawFraming => _stream.UsesRawFraming;

    /// <inheritdoc />
    public bool CanCloseStandardInput => _stream.CanCloseStandardInput;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public async Task<ExecReadResult> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read.EndOfStream)
        {
            IsRunning = false;
        }

        return new ExecReadResult(Map(read.Target), read.Count);
    }

    /// <inheritdoc />
    public Task SendAsync(string text, CancellationToken cancellationToken = default) =>
        _stream.WriteAsync(text, cancellationToken);

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        _stream.WriteAsync(data, cancellationToken);

    /// <inheritdoc />
    public Task ResizeAsync(int rows, int columns, CancellationToken cancellationToken = default) =>
        //ResizeExecAsync takes height first; this is the one argument order worth naming.
        _containers.ResizeExecAsync(_stream.ExecId, height: rows, width: columns, cancellationToken);

    /// <inheritdoc />
    public Task CloseStandardInputAsync(CancellationToken cancellationToken = default) =>
        _stream.CloseStandardInputAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        var code = await _stream.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        IsRunning = false;
        return code;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            IsRunning = false;
            _stream.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            IsRunning = false;
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ExecStreamKind Map(ExecStreamTarget target) => target switch
    {
        ExecStreamTarget.StandardOutput => ExecStreamKind.StandardOutput,
        ExecStreamTarget.StandardError => ExecStreamKind.StandardError,
        _ => ExecStreamKind.None,
    };
}
