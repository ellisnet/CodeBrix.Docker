using System.Collections.Generic;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>The result of verifying that a deployment is what it claims to be.</summary>
public sealed class RedisTopologyVerification
{
    /// <summary>Gets the shape that was verified.</summary>
    public RedisConnectionShape Shape { get; init; }

    /// <summary>Gets a value indicating whether every check passed.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the checks, in the order they ran; never null.</summary>
    public IReadOnlyList<RedisVerificationCheck> Checks { get; init; } = [];

    /// <summary>Gets a one-line summary.</summary>
    public string Summary { get; init; }
}
