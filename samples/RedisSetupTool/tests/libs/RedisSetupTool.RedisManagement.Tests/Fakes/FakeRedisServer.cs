using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Tests.Fakes;

/// <summary>
/// One node's worth of behaviour: a key-value store plus canned replies for the informational
/// commands, with injectable latency and failure. This is what makes the lock timing tests
/// deterministic and the verification tests daemon-free.
/// </summary>
public sealed class FakeRedisServer : IRedisConnection
{
    private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);
    private readonly List<ScriptCall> _scriptCalls = [];

    /// <summary>Creates a node.</summary>
    /// <param name="description">What the node is called in messages.</param>
    public FakeRedisServer(string description = "fake")
    {
        Description = description;
    }

    /// <summary>One recorded script evaluation.</summary>
    /// <param name="Script">The script text.</param>
    /// <param name="Keys">The keys.</param>
    /// <param name="Values">The arguments.</param>
    public sealed record ScriptCall(string Script, string[] Keys, object[] Values);

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>Gets the canned replies, keyed by the whole command line.</summary>
    public Dictionary<string, string> Replies { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets how long every operation takes.</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>Gets or sets the failure every operation raises.</summary>
    public Exception Failure { get; set; }

    /// <summary>Gets or sets a value indicating whether a conditional set is refused.</summary>
    public bool RefuseSetNotExists { get; set; }

    /// <summary>Gets the script evaluations that were recorded.</summary>
    public IReadOnlyList<ScriptCall> ScriptCalls => _scriptCalls;

    /// <summary>Gets the stored values.</summary>
    public IReadOnlyDictionary<string, string> Data => _data;

    /// <summary>Gets a value indicating whether the connection was disposed.</summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc />
    public async Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        await GateAsync().ConfigureAwait(false);
        return Latency;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string command, params object[] args)
    {
        await GateAsync().ConfigureAwait(false);

        var line = command;
        foreach (var argument in args ?? [])
        {
            line += " " + argument;
        }

        if (Replies.TryGetValue(line, out var reply))
        {
            return reply;
        }

        return Replies.TryGetValue(command, out var fallback) ? fallback : string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null,
        bool onlyIfAbsent = false)
    {
        await GateAsync().ConfigureAwait(false);

        if (onlyIfAbsent && (RefuseSetNotExists || _data.ContainsKey(key)))
        {
            return false;
        }

        _data[key] = value;
        return true;
    }

    /// <inheritdoc />
    public async Task<string> StringGetAsync(string key)
    {
        await GateAsync().ConfigureAwait(false);
        return _data.TryGetValue(key, out var value) ? value : null;
    }

    /// <inheritdoc />
    public async Task<bool> KeyDeleteAsync(string key)
    {
        await GateAsync().ConfigureAwait(false);
        return _data.Remove(key);
    }

    /// <inheritdoc />
    public async Task<long> ScriptEvaluateLongAsync(string script, string[] keys, object[] values)
    {
        await GateAsync().ConfigureAwait(false);
        _scriptCalls.Add(new ScriptCall(script, keys ?? [], values ?? []));

        var key = keys is { Length: > 0 } ? keys[0] : null;
        var token = values is { Length: > 0 } ? values[0]?.ToString() : null;

        if (key is null || !_data.TryGetValue(key, out var stored)
            || !string.Equals(stored, token, StringComparison.Ordinal))
        {
            return 0;
        }

        if (script.Contains("PEXPIRE", StringComparison.Ordinal))
        {
            return 1;
        }

        _data.Remove(key);
        return 1;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private async Task GateAsync()
    {
        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency).ConfigureAwait(false);
        }

        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
