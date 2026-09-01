namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One step of a create or destroy run.</summary>
public sealed class TopologyProgress
{
    /// <summary>Gets the one-based step number.</summary>
    public int Step { get; init; }

    /// <summary>Gets how many steps there are.</summary>
    public int TotalSteps { get; init; }

    /// <summary>Gets what is happening.</summary>
    public string Message { get; init; }

    /// <summary>Gets a value indicating whether the step reports a failure.</summary>
    public bool IsFailure { get; init; }
}
