namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How badly a container is being CPU throttled.</summary>
public enum ThrottleLevel
{
    /// <summary>No meaningful throttling.</summary>
    None = 0,

    /// <summary>Some throttling; usually harmless.</summary>
    Moderate = 1,

    /// <summary>Enough throttling to be felt.</summary>
    High = 2,

    /// <summary>Throttling dominates; the CPU limit is too low.</summary>
    Critical = 3,
}
