namespace RedisSetupTool.RedisManagement.Exercises;

/// <summary>How hard to exercise a deployment.</summary>
public sealed class RedisExerciseOptions
{
    /// <summary>Gets or sets the prefix every key the run creates carries.</summary>
    public string KeyPrefix { get; set; } = "redissetup:probe";

    /// <summary>Gets or sets how many keys the run writes.</summary>
    public int KeyCount { get; set; } = 20;

    /// <summary>Gets or sets a value indicating whether lists and hashes are exercised too.</summary>
    public bool IncludeDataTypes { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a pipelined batch is exercised.</summary>
    public bool IncludePipeline { get; set; } = true;
}
