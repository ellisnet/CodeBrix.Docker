using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Exercises;
using RedisSetupTool.RedisManagement.Results;

namespace RedisSetupTool.RedisManagement;

/// <summary>Asks a live deployment what it is and whether it works.</summary>
public interface IRedisProbe
{
    /// <summary>Checks reachability.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The result; a failure is reported, not thrown.</returns>
    Task<RedisPingResult> PingAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what the node says about itself.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server information.</returns>
    Task<RedisServerInfo> GetServerInfoAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Checks that the deployment is the shape it claims to be.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One check per thing that was proved.</returns>
    Task<RedisTopologyVerification> VerifyAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the replication picture.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The replication view.</returns>
    Task<RedisReplicationView> GetReplicationAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the cluster picture.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The cluster view.</returns>
    Task<RedisClusterView> GetClusterAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the loaded modules.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The modules.</returns>
    Task<IReadOnlyList<RedisModuleInfo>> GetModulesAsync(RedisConnectionDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Puts the deployment through a round of real work.</summary>
    /// <param name="descriptor">What to dial.</param>
    /// <param name="options">How hard to push; null selects the defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One step per thing that was tried.</returns>
    Task<RedisExerciseResult> ExerciseAsync(RedisConnectionDescriptor descriptor,
        RedisExerciseOptions options = null, CancellationToken cancellationToken = default);
}
