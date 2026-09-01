using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>
/// A live shell inside a container. Bytes pass through untouched in both directions: nothing here
/// decodes text, because a read can split a multi-byte sequence and the terminal control takes bytes.
/// </summary>
public interface IExecSession : IAsyncDisposable, IDisposable
{
    /// <summary>Gets the container the shell runs in.</summary>
    string ContainerId { get; }

    /// <summary>Gets the daemon's exec id.</summary>
    string ExecId { get; }

    /// <summary>Gets the shell that was resolved.</summary>
    string ShellPath { get; }

    /// <summary>Gets a value indicating whether a terminal was requested.</summary>
    bool IsTty { get; }

    /// <summary>Gets a value indicating whether the daemon answered with raw framing.</summary>
    bool UsesRawFraming { get; }

    /// <summary>Gets a value indicating whether standard input can be half-closed.</summary>
    bool CanCloseStandardInput { get; }

    /// <summary>Gets a value indicating whether the session is still open.</summary>
    bool IsRunning { get; }

    /// <summary>Reads the next chunk of output.</summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was read; a count of zero means the session closed.</returns>
    Task<ExecReadResult> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>Sends text to the shell, encoded as UTF-8 with no terminator added.</summary>
    /// <param name="text">The text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the bytes are written.</returns>
    Task SendAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Sends bytes to the shell, untouched.</summary>
    /// <param name="data">The bytes to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the bytes are written.</returns>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Resizes the terminal inside the container.</summary>
    /// <param name="rows">The new row count.</param>
    /// <param name="columns">The new column count.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon accepts the size.</returns>
    Task ResizeAsync(int rows, int columns, CancellationToken cancellationToken = default);

    /// <summary>Half-closes standard input, which is how a filter sees end of file.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the write half is shut.</returns>
    Task CloseStandardInputAsync(CancellationToken cancellationToken = default);

    /// <summary>Waits for the shell to exit and reports its code.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exit code.</returns>
    Task<long> WaitForExitAsync(CancellationToken cancellationToken = default);
}
