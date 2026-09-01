using System;
using System.Collections.Generic;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>The result of putting a deployment through its paces.</summary>
public sealed class RedisExerciseResult
{
    /// <summary>Gets a value indicating whether every step passed.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets the steps, in order; never null.</summary>
    public IReadOnlyList<RedisExerciseStep> Steps { get; init; } = [];

    /// <summary>Gets how long the whole run took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Gets a one-line summary.</summary>
    public string Summary { get; init; }
}
