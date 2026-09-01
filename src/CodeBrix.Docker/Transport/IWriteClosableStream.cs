// Adapted from Docker.DotNet (https://github.com/dotnet/Docker.DotNet), MIT License, Copyright (c) .NET Foundation and Contributors.
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// A bidirectional stream whose writing half can be closed on its own, leaving the reading half open.
/// </summary>
/// <remarks>
/// This is how end-of-input is signalled on a hijacked <c>exec</c> or <c>attach</c> connection: the
/// process inside the container sees standard input reach end of file while the daemon keeps sending
/// its output back on the same connection.
/// </remarks>
internal interface IWriteClosableStream
{
    /// <summary>Gets a value indicating whether the writing half can be closed independently.</summary>
    bool CanCloseWrite { get; }

    /// <summary>Closes the writing half, leaving the reading half open.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the shutdown has been requested.</returns>
    Task CloseWriteAsync(CancellationToken cancellationToken = default);
}
