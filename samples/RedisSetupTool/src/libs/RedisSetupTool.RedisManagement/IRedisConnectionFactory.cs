using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement;

/// <summary>Opens connections. The only implementation is the one that touches the client library.</summary>
public interface IRedisConnectionFactory
{
    /// <summary>Connects to a whole deployment, following its shape.</summary>
    /// <param name="descriptor">What to connect to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connection.</returns>
    Task<IRedisConnection> ConnectAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Connects to one endpoint, ignoring the deployment it belongs to.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="credentials">The credentials.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connection.</returns>
    Task<IRedisConnection> ConnectSingleAsync(RedisHostPort endpoint, RedisCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>Connects to the sentinels themselves, rather than to the master they monitor.</summary>
    /// <param name="descriptor">The sentinel descriptor.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connection.</returns>
    /// <remarks>
    /// A sentinel speaks a reduced command set, so an ordinary connection handshake fails against
    /// one; this path uses the client library's sentinel command map.
    /// </remarks>
    Task<IRedisConnection> ConnectSentinelsAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
