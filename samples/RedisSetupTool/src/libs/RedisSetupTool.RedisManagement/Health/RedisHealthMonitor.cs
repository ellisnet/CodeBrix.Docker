using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Health;

/// <summary>
/// The default monitor. It is what turns an instance card's state pill green: the containers being up
/// is not the same thing as Redis answering.
/// </summary>
public sealed class RedisHealthMonitor : IRedisHealthMonitor
{
    private readonly IRedisProbe _probe;

    /// <summary>Creates the monitor.</summary>
    /// <param name="probe">The probe used to read the node.</param>
    public RedisHealthMonitor(IRedisProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <inheritdoc />
    public async Task<RedisHealthSample> SampleAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var ping = await _probe.PingAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (!ping.Succeeded)
        {
            return new RedisHealthSample
            {
                Reachable = false,
                SampledAt = DateTimeOffset.UtcNow,
                ErrorMessage = ping.ErrorMessage,
            };
        }

        try
        {
            var info = await _probe.GetServerInfoAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);
            return new RedisHealthSample
            {
                Reachable = true,
                RoundTrip = ping.RoundTrip,
                Role = info.Role,
                UsedMemoryBytes = info.UsedMemoryBytes,
                ConnectedClients = info.ConnectedClients,
                SampledAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception exception)
        {
            return new RedisHealthSample
            {
                Reachable = true,
                RoundTrip = ping.RoundTrip,
                SampledAt = DateTimeOffset.UtcNow,
                ErrorMessage = exception.Message,
            };
        }
    }
}
