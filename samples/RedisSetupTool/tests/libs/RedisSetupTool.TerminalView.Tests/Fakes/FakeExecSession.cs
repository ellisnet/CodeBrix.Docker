using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Exec;

namespace RedisSetupTool.TerminalView.Tests.Fakes;

/// <summary>An exec session driven by the test rather than by a daemon.</summary>
public sealed class FakeExecSession : IExecSession
{
    private readonly BlockingCollection<byte[]> _pending = [];
    private readonly List<string> _sent = [];
    private readonly List<(int Rows, int Columns)> _resizes = [];
    private readonly TaskCompletionSource<long> _exit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets or sets the exception the next read throws.</summary>
    public Exception ReadFailure { get; set; }

    /// <summary>Gets or sets the code the shell exits with.</summary>
    public long ExitCode { get; set; }

    /// <summary>Gets what was sent to the shell.</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    /// <summary>Gets the resizes that were requested, in (rows, columns) order.</summary>
    public IReadOnlyList<(int Rows, int Columns)> Resizes
    {
        get
        {
            lock (_resizes)
            {
                return _resizes.ToArray();
            }
        }
    }

    /// <summary>Gets a value indicating whether the session was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public string ContainerId { get; init; } = "container";

    /// <inheritdoc />
    public string ExecId { get; init; } = "exec";

    /// <inheritdoc />
    public string ShellPath { get; init; } = "/bin/sh";

    /// <inheritdoc />
    public bool IsTty => true;

    /// <inheritdoc />
    public bool UsesRawFraming => true;

    /// <inheritdoc />
    public bool CanCloseStandardInput => true;

    /// <inheritdoc />
    public bool IsRunning { get; private set; } = true;

    /// <summary>Queues bytes for the pump to read.</summary>
    /// <param name="data">The bytes.</param>
    public void Emit(params byte[] data) => _pending.Add(data);

    /// <summary>Signals that the shell has closed its output.</summary>
    public void EndOfStream() => _pending.CompleteAdding();

    /// <inheritdoc />
    public Task<ExecReadResult> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (ReadFailure is not null)
        {
            var failure = ReadFailure;
            ReadFailure = null;
            throw failure;
        }

        try
        {
            if (!_pending.TryTake(out var data, Timeout.Infinite, cancellationToken))
            {
                IsRunning = false;
                _exit.TrySetResult(ExitCode);
                return Task.FromResult(new ExecReadResult(ExecStreamKind.None, 0));
            }

            data.AsSpan().CopyTo(buffer.Span);
            return Task.FromResult(new ExecReadResult(ExecStreamKind.StandardOutput, data.Length));
        }
        catch (InvalidOperationException)
        {
            IsRunning = false;
            _exit.TrySetResult(ExitCode);
            return Task.FromResult(new ExecReadResult(ExecStreamKind.None, 0));
        }
    }

    /// <inheritdoc />
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        lock (_sent)
        {
            _sent.Add(text);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        SendAsync(System.Text.Encoding.UTF8.GetString(data.Span), cancellationToken);

    /// <inheritdoc />
    public Task ResizeAsync(int rows, int columns, CancellationToken cancellationToken = default)
    {
        lock (_resizes)
        {
            _resizes.Add((rows, columns));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseStandardInputAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<long> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        _exit.TrySetResult(ExitCode);
        return _exit.Task;
    }

    /// <inheritdoc />
    public void Dispose() => Disposed = true;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _pending.Dispose();
        return ValueTask.CompletedTask;
    }
}
