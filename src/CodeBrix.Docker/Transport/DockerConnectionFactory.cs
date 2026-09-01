using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Builds the <see cref="SocketsHttpHandler"/> that carries Docker Engine API traffic over a
/// named pipe, a Unix domain socket, or a plain TCP connection.
/// </summary>
internal static class DockerConnectionFactory
{
    /// <summary>
    /// Creates a handler whose <see cref="SocketsHttpHandler.ConnectCallback"/> dials
    /// <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="endpoint">The parsed daemon endpoint.</param>
    /// <returns>A configured handler. The caller owns its lifetime.</returns>
    public static SocketsHttpHandler CreateHandler(DockerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new SocketsHttpHandler
        {
            ConnectCallback = CreateConnectCallback(endpoint),
            // The daemon does not benefit from connection pooling heuristics tuned for the public web,
            // and hijacked streams (exec, attach) must not be recycled underneath us.
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = int.MaxValue,
            UseProxy = false,
            AllowAutoRedirect = false,
        };
    }

    /// <summary>
    /// Creates the connect callback for <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="endpoint">The parsed daemon endpoint.</param>
    /// <returns>A callback returning a connected stream.</returns>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback(
        DockerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return endpoint.Kind switch
        {
            DockerEndpointKind.NamedPipe => (_, ct) => ConnectNamedPipeAsync(endpoint, ct),
            DockerEndpointKind.UnixSocket => (_, ct) => ConnectUnixSocketAsync(endpoint, ct),
            DockerEndpointKind.Tcp => (_, ct) => ConnectTcpAsync(endpoint, ct),
            _ => throw new NotSupportedException($"The endpoint kind '{endpoint.Kind}' is not supported."),
        };
    }

    private static async ValueTask<Stream> ConnectNamedPipeAsync(DockerEndpoint endpoint, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(
            endpoint.PipeServer,
            endpoint.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);

        try
        {
            await pipe.ConnectAsync(ct).ConfigureAwait(false);
            return pipe;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new DockerException(
                $"Could not connect to the Docker daemon on named pipe '{endpoint.PipeName}'. Is Docker running?", ex);
        }
    }

    private static async ValueTask<Stream> ConnectUnixSocketAsync(DockerEndpoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new DockerException(
                $"Could not connect to the Docker daemon on socket '{endpoint.SocketPath}'. Is Docker running?", ex);
        }
    }

    private static async ValueTask<Stream> ConnectTcpAsync(DockerEndpoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            socket.Dispose();
            throw new DockerException(
                $"Could not connect to the Docker daemon at {endpoint.Host}:{endpoint.Port}. Is Docker running?", ex);
        }
    }
}
