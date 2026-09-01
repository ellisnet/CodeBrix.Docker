using System;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>One step of an exercise run.</summary>
public sealed class RedisExerciseStep
{
    /// <summary>Gets what the step did.</summary>
    public string Name { get; init; }

    /// <summary>Gets a value indicating whether the step passed.</summary>
    public bool Passed { get; init; }

    /// <summary>Gets what was observed.</summary>
    public string Detail { get; init; }

    /// <summary>Gets how long the step took.</summary>
    public TimeSpan Elapsed { get; init; }
}
