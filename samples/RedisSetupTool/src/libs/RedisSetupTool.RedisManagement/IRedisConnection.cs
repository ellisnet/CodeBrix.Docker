using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement;

/// <summary>
/// The seam that keeps the Redis client in one file. Everything above this - the probe, the lock
/// service, the health monitor - talks to this interface, which is what lets them be tested with a
/// fake and no daemon, and what made the swap from StackExchange.Redis to CodeBrix.Redis a
/// one-project change.
/// </summary>
public interface IRedisConnection : IAsyncDisposable
{
    /// <summary>Gets a human description of what is connected, for messages.</summary>
    string Description { get; }

    /// <summary>Measures a round trip.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>How long the round trip took.</returns>
    Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a raw command and returns its reply as text.</summary>
    /// <param name="command">The command name.</param>
    /// <param name="args">The command arguments.</param>
    /// <returns>The reply; array replies are joined with newlines.</returns>
    Task<string> ExecuteAsync(string command, params object[] args);

    /// <summary>Sets a string value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expiry">How long the key lives; null means forever.</param>
    /// <param name="onlyIfAbsent">Whether the set happens only when the key does not exist.</param>
    /// <returns>True when the value was stored.</returns>
    Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null,
        bool onlyIfAbsent = false);

    /// <summary>Reads a string value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or null when the key is absent.</returns>
    Task<string> StringGetAsync(string key);

    /// <summary>Deletes a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when a key was removed.</returns>
    Task<bool> KeyDeleteAsync(string key);

    /// <summary>Runs a Lua script that returns an integer.</summary>
    /// <param name="script">The script.</param>
    /// <param name="keys">The keys the script touches.</param>
    /// <param name="values">The script arguments.</param>
    /// <returns>The integer the script returned.</returns>
    Task<long> ScriptEvaluateLongAsync(string script, string[] keys, object[] values);
}
