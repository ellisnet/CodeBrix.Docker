using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Tests.Fakes;

/// <summary>Hands out <see cref="FakeRedisServer"/> instances keyed by endpoint.</summary>
public sealed class FakeRedisConnectionFactory : IRedisConnectionFactory
{
    private readonly Dictionary<string, FakeRedisServer> _servers = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the node returned when no endpoint matches.</summary>
    public FakeRedisServer Default { get; set; }

    /// <summary>Gets or sets the node returned for a sentinel connection.</summary>
    public Func<RedisConnectionDescriptor, FakeRedisServer> SentinelSelector { get; set; }

    /// <summary>Gets how many connections were opened.</summary>
    public int ConnectCount { get; private set; }

    /// <summary>Registers a node for one endpoint.</summary>
    /// <param name="endpoint">The endpoint text, for example <c>127.0.0.1:6401</c>.</param>
    /// <param name="server">The node.</param>
    /// <returns>The same factory, for chaining.</returns>
    public FakeRedisConnectionFactory With(string endpoint, FakeRedisServer server)
    {
        _servers[endpoint] = server;
        return this;
    }

    /// <summary>Gets the node registered for an endpoint.</summary>
    /// <param name="endpoint">The endpoint text.</param>
    /// <returns>The node.</returns>
    public FakeRedisServer Server(string endpoint) => _servers[endpoint];

    /// <inheritdoc />
    public Task<IRedisConnection> ConnectAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        var key = descriptor.Endpoints.Count > 0 ? descriptor.Endpoints[0].ToString() : string.Empty;
        return Task.FromResult<IRedisConnection>(Resolve(key));
    }

    /// <inheritdoc />
    public Task<IRedisConnection> ConnectSingleAsync(RedisHostPort endpoint,
        RedisCredentials credentials, CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        return Task.FromResult<IRedisConnection>(Resolve(endpoint.ToString()));
    }

    /// <inheritdoc />
    public Task<IRedisConnection> ConnectSentinelsAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        var selected = SentinelSelector?.Invoke(descriptor);
        if (selected is not null)
        {
            return Task.FromResult<IRedisConnection>(selected);
        }

        var key = descriptor.Endpoints.Count > 0 ? descriptor.Endpoints[0].ToString() : string.Empty;
        return Task.FromResult<IRedisConnection>(Resolve(key));
    }

    private FakeRedisServer Resolve(string key) =>
        _servers.TryGetValue(key, out var server)
            ? server
            : Default ?? throw new InvalidOperationException("No fake node registered for " + key);
}
