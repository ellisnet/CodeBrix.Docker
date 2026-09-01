using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Redlock;

/// <summary>
/// The Redlock algorithm, implemented exactly: take the key on every master in parallel with a short
/// per-node timeout, subtract the elapsed time and a drift allowance from the lifetime, and call the
/// lock held only when a quorum granted it and time is left over.
/// </summary>
public sealed class RedlockService : IRedlockService
{
    private readonly IRedisConnectionFactory _factory;

    /// <summary>Creates the service.</summary>
    /// <param name="factory">The connection factory.</param>
    public RedlockService(IRedisConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public async Task<RedlockHandle> AcquireAsync(IReadOnlyList<RedisHostPort> masters,
        RedisCredentials credentials, string resource, TimeSpan ttl, RedlockOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(masters);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (masters.Count == 0)
        {
            throw new ArgumentException("A quorum needs at least one master.", nameof(masters));
        }

        var settings = options ?? new RedlockOptions();
        var lifetime = ttl <= TimeSpan.Zero ? settings.DefaultTtl : ttl;
        var quorum = (masters.Count / 2) + 1;
        var token = Guid.NewGuid().ToString("N");
        var nodeTimeout = settings.ResolveNodeTimeout(lifetime);
        var milliseconds = (long)lifetime.TotalMilliseconds;

        var connections = new List<IRedisConnection>(masters.Count);
        var endpoints = new List<string>(masters.Count);

        try
        {
            foreach (var master in masters)
            {
                endpoints.Add(master.ToString());
                connections.Add(await _factory.ConnectSingleAsync(master, credentials,
                    cancellationToken).ConfigureAwait(false));
            }
        }
        catch (Exception)
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }

        var attempts = Math.Max(1, settings.RetryCount);
        RedlockHandle handle = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var clock = Stopwatch.StartNew();
            var acquisitions = await TakeAsync(connections, endpoints, resource, token, milliseconds,
                nodeTimeout).ConfigureAwait(false);
            var elapsed = clock.Elapsed;

            var acquired = 0;
            foreach (var acquisition in acquisitions)
            {
                if (acquisition.Acquired)
                {
                    acquired++;
                }
            }

            var driftFloor = settings.ClockDriftFloor;
            var driftShare = TimeSpan.FromMilliseconds(
                lifetime.TotalMilliseconds * settings.ClockDriftFactor);
            var drift = driftShare > driftFloor ? driftShare : driftFloor;
            var validity = lifetime - elapsed - drift;

            handle = new RedlockHandle(resource, token, quorum, validity, acquisitions, connections,
                acquired >= quorum && validity > TimeSpan.Zero);

            if (handle.IsHeld)
            {
                return handle;
            }

            //Not held: give back whatever was taken, including the masters that did grant it.
            await ReleaseEverywhereAsync(connections, resource, token).ConfigureAwait(false);

            if (attempt < attempts - 1)
            {
                var jitter = Random.Shared.NextDouble() * settings.RetryDelay.TotalMilliseconds;
                await Task.Delay(settings.RetryDelay + TimeSpan.FromMilliseconds(jitter),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return handle;
    }

    private static async Task<IReadOnlyList<RedlockAcquisition>> TakeAsync(
        IReadOnlyList<IRedisConnection> connections, IReadOnlyList<string> endpoints, string resource,
        string token, long milliseconds, TimeSpan nodeTimeout)
    {
        var tasks = new Task<RedlockAcquisition>[connections.Count];
        for (var index = 0; index < connections.Count; index++)
        {
            tasks[index] = TakeOneAsync(connections[index], endpoints[index], resource, token,
                milliseconds, nodeTimeout);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<RedlockAcquisition> TakeOneAsync(IRedisConnection connection,
        string endpoint, string resource, string token, long milliseconds, TimeSpan nodeTimeout)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var set = connection.StringSetAsync(resource, token,
                TimeSpan.FromMilliseconds(milliseconds), onlyIfAbsent: true);
            var finished = await Task.WhenAny(set, Task.Delay(nodeTimeout)).ConfigureAwait(false);

            if (!ReferenceEquals(finished, set))
            {
                return new RedlockAcquisition
                {
                    Endpoint = endpoint,
                    Acquired = false,
                    RoundTrip = clock.Elapsed,
                    ErrorMessage = "Timed out after "
                        + nodeTimeout.TotalMilliseconds.ToString("0") + " ms.",
                };
            }

            return new RedlockAcquisition
            {
                Endpoint = endpoint,
                Acquired = await set.ConfigureAwait(false),
                RoundTrip = clock.Elapsed,
            };
        }
        catch (Exception exception)
        {
            return new RedlockAcquisition
            {
                Endpoint = endpoint,
                Acquired = false,
                RoundTrip = clock.Elapsed,
                ErrorMessage = exception.Message,
            };
        }
    }

    private static async Task ReleaseEverywhereAsync(IReadOnlyList<IRedisConnection> connections,
        string resource, string token)
    {
        foreach (var connection in connections)
        {
            try
            {
                await connection.ScriptEvaluateLongAsync(RedlockHandle.ReleaseScript, [resource],
                    [token]).ConfigureAwait(false);
            }
            catch (Exception)
            {
                //Best effort: a key nobody could delete expires on its own.
            }
        }
    }
}
