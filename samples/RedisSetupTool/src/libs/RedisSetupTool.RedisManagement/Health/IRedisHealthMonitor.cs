using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Health;

/// <summary>Samples a deployment's health on a cadence the caller owns.</summary>
public interface IRedisHealthMonitor
{
    /// <summary>Takes one reading.</summary>
    /// <param name="descriptor">What to sample.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reading; a failure is reported, not thrown.</returns>
    Task<RedisHealthSample> SampleAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
