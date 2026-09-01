namespace RedisSetupTool.RedisManagement.Results;

/// <summary>One loaded module.</summary>
public sealed class RedisModuleInfo
{
    /// <summary>Gets the module name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the module version.</summary>
    public string Version { get; init; }
}
