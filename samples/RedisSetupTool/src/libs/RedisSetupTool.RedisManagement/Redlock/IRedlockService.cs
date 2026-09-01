using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.RedisManagement.Redlock;

/// <summary>Acquires a lock across independent masters.</summary>
public interface IRedlockService
{
    /// <summary>Tries to take a lock on a quorum of masters.</summary>
    /// <param name="masters">The masters; a quorum is half of them plus one.</param>
    /// <param name="credentials">The shared credentials.</param>
    /// <param name="resource">The key the lock names.</param>
    /// <param name="ttl">How long the lock should live.</param>
    /// <param name="options">Retry and drift settings; null selects the defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The handle, whose <see cref="RedlockHandle.IsHeld"/> says whether it worked.</returns>
    Task<RedlockHandle> AcquireAsync(IReadOnlyList<RedisHostPort> masters,
        RedisCredentials credentials, string resource, TimeSpan ttl, RedlockOptions options = null,
        CancellationToken cancellationToken = default);
}
