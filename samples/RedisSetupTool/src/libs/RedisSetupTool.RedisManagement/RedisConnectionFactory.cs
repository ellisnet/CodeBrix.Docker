using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Redis;

namespace RedisSetupTool.RedisManagement;

/// <summary>
/// The one type that names <c>ConnectionMultiplexer</c>. Everything else in the library works through
/// <see cref="IRedisConnection"/>.
/// </summary>
public sealed class RedisConnectionFactory : IRedisConnectionFactory
{
    /// <inheritdoc />
    public async Task<IRedisConnection> ConnectAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        if (descriptor.Shape == RedisConnectionShape.Sentinel)
        {
            var sentinelOptions = RedisConnectionStringBuilder.BuildOptions(descriptor);
            var sentinel = await ConnectionMultiplexer.SentinelConnectAsync(sentinelOptions)
                .ConfigureAwait(false);
            var master = sentinel.GetSentinelMasterConnection(
                RedisConnectionStringBuilder.BuildSentinelMasterOptions(descriptor));
            return new RedisConnection(master, sentinel, Describe(descriptor));
        }

        var options = RedisConnectionStringBuilder.BuildOptions(descriptor);
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        return new RedisConnection(multiplexer, null, Describe(descriptor));
    }

    /// <inheritdoc />
    public Task<IRedisConnection> ConnectSingleAsync(RedisHostPort endpoint,
        RedisCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return ConnectAsync(new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints = [endpoint],
            Credentials = credentials,
        }, cancellationToken);
    }

    /// <summary>Opens a sentinel-only connection, for asking sentinels about themselves.</summary>
    /// <param name="descriptor">The sentinel descriptor.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A connection to the sentinels themselves, not to the master they monitor.</returns>
    public async Task<IRedisConnection> ConnectSentinelsAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();

        var options = RedisConnectionStringBuilder.BuildOptions(descriptor);
        var sentinel = await ConnectionMultiplexer.SentinelConnectAsync(options)
            .ConfigureAwait(false);
        return new RedisConnection(sentinel, null, "sentinels of " + descriptor.ServiceName);
    }

    private static string Describe(RedisConnectionDescriptor descriptor)
    {
        var text = new StringBuilder(descriptor.Shape.ToString());
        text.Append(" [");
        for (var index = 0; index < descriptor.Endpoints.Count; index++)
        {
            if (index > 0)
            {
                text.Append(", ");
            }

            text.Append(descriptor.Endpoints[index]);
        }

        text.Append(']');
        return text.ToString();
    }
}
