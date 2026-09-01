namespace RedisSetupTool.RedisManagement.Results;

/// <summary>One thing that was checked, and how it went.</summary>
public sealed class RedisVerificationCheck
{
    /// <summary>Gets what was checked.</summary>
    public string Name { get; init; }

    /// <summary>Gets a value indicating whether the check passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Gets what was observed.</summary>
    public string Detail { get; init; }
}
