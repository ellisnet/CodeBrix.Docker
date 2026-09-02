using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Redis;

namespace RedisSetupTool.RedisManagement;

/// <summary>Wraps one multiplexer, and the sentinel connection it came from when there is one.</summary>
internal sealed class RedisConnection : IRedisConnection
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IConnectionMultiplexer _owner;
    private int _disposed;

    internal RedisConnection(IConnectionMultiplexer multiplexer, IConnectionMultiplexer owner,
        string description)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _owner = owner;
        Description = description;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public async Task<TimeSpan> PingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _multiplexer.GetDatabase().PingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string command, params object[] args)
    {
        var result = await _multiplexer.GetDatabase().ExecuteAsync(command, args ?? [])
            .ConfigureAwait(false);
        return Stringify(result);
    }

    /// <inheritdoc />
    public Task<bool> StringSetAsync(string key, string value, TimeSpan? expiry = null,
        bool onlyIfAbsent = false) =>
        _multiplexer.GetDatabase().StringSetAsync(key, value, expiry,
            onlyIfAbsent ? When.NotExists : When.Always);

    /// <inheritdoc />
    public async Task<string> StringGetAsync(string key)
    {
        var value = await _multiplexer.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    /// <inheritdoc />
    public Task<bool> KeyDeleteAsync(string key) => _multiplexer.GetDatabase().KeyDeleteAsync(key);

    /// <inheritdoc />
    public async Task<long> ScriptEvaluateLongAsync(string script, string[] keys, object[] values)
    {
        var redisKeys = new RedisKey[keys?.Length ?? 0];
        for (var index = 0; index < redisKeys.Length; index++)
        {
            redisKeys[index] = keys[index];
        }

        var redisValues = new RedisValue[values?.Length ?? 0];
        for (var index = 0; index < redisValues.Length; index++)
        {
            redisValues[index] = values[index]?.ToString() ?? string.Empty;
        }

        var result = await _multiplexer.GetDatabase()
            .ScriptEvaluateAsync(script, redisKeys, redisValues).ConfigureAwait(false);
        return result.IsNull ? 0L : (long)result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _multiplexer.CloseAsync().ConfigureAwait(false);
        _multiplexer.Dispose();

        if (_owner is not null)
        {
            await _owner.CloseAsync().ConfigureAwait(false);
            _owner.Dispose();
        }
    }

    private static string Stringify(RedisResult result)
    {
        if (result is null || result.IsNull)
        {
            return string.Empty;
        }

        try
        {
            var items = (RedisResult[])result;
            if (items is not null)
            {
                var text = new StringBuilder();
                foreach (var item in items)
                {
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }

                    text.Append(Stringify(item));
                }

                return text.ToString();
            }
        }
        catch (InvalidCastException)
        {
            //Not an array reply; fall through to the scalar rendering.
        }

        return result.ToString();
    }
}
