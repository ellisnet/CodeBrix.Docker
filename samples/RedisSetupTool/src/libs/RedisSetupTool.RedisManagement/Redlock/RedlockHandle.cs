using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Redlock;

/// <summary>
/// A lock held - or not held - across a set of independent masters. Release and extend both run a
/// compare-and-set script, so a stale token can never unlock somebody else's lock.
/// </summary>
public sealed class RedlockHandle : IAsyncDisposable
{
    /// <summary>The script that deletes a key only when it still carries the caller's token.</summary>
    public const string ReleaseScript =
        "if redis.call(\"GET\", KEYS[1]) == ARGV[1] then return redis.call(\"DEL\", KEYS[1]) "
        + "else return 0 end";

    /// <summary>The script that re-dates a key only when it still carries the caller's token.</summary>
    public const string ExtendScript =
        "if redis.call(\"GET\", KEYS[1]) == ARGV[1] then "
        + "return redis.call(\"PEXPIRE\", KEYS[1], ARGV[2]) else return 0 end";

    private readonly IReadOnlyList<IRedisConnection> _connections;
    private int _released;
    private int _disposed;

    internal RedlockHandle(string resource, string token, int quorum, TimeSpan validity,
        IReadOnlyList<RedlockAcquisition> acquisitions, IReadOnlyList<IRedisConnection> connections,
        bool isHeld)
    {
        Resource = resource;
        Token = token;
        Quorum = quorum;
        Validity = validity;
        Acquisitions = acquisitions ?? [];
        _connections = connections ?? [];
        IsHeld = isHeld;

        var acquired = 0;
        foreach (var acquisition in Acquisitions)
        {
            if (acquisition.Acquired)
            {
                acquired++;
            }
        }

        AcquiredCount = acquired;
    }

    /// <summary>Gets the resource the lock names.</summary>
    public string Resource { get; }

    /// <summary>Gets the random value that proves ownership.</summary>
    public string Token { get; }

    /// <summary>Gets how many masters granted the lock.</summary>
    public int AcquiredCount { get; }

    /// <summary>Gets how many masters had to grant it.</summary>
    public int Quorum { get; }

    /// <summary>Gets a value indicating whether the lock is held.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Gets how long the lock stays valid, after elapsed time and drift.</summary>
    public TimeSpan Validity { get; }

    /// <summary>Gets what each master said; never null.</summary>
    public IReadOnlyList<RedlockAcquisition> Acquisitions { get; }

    /// <summary>Re-dates the lock on every master that still carries the token.</summary>
    /// <param name="ttl">The new lifetime.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when a quorum of masters extended it.</returns>
    public async Task<bool> ExtendAsync(TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (!IsHeld)
        {
            return false;
        }

        var milliseconds = (long)ttl.TotalMilliseconds;
        var extended = 0;

        foreach (var connection in _connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await connection.ScriptEvaluateLongAsync(ExtendScript, [Resource],
                    [Token, milliseconds]).ConfigureAwait(false);
                if (result == 1)
                {
                    extended++;
                }
            }
            catch (Exception)
            {
                //A master that cannot answer simply does not count toward the quorum.
            }
        }

        if (extended < Quorum)
        {
            IsHeld = false;
            return false;
        }

        return true;
    }

    /// <summary>Releases the lock everywhere, whether or not a quorum was reached.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every master has been asked.</returns>
    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        foreach (var connection in _connections)
        {
            try
            {
                await connection.ScriptEvaluateLongAsync(ReleaseScript, [Resource], [Token])
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                //Best effort: an unreachable master's key expires on its own.
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        IsHeld = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var connection in _connections)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                //Disposal is best effort.
            }
        }
    }
}
