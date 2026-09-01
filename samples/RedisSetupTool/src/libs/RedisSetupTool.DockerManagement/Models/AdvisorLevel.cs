namespace RedisSetupTool.DockerManagement.Models;

/// <summary>How urgently an advisor finding needs attention.</summary>
public enum AdvisorLevel
{
    /// <summary>Worth knowing.</summary>
    Info = 0,

    /// <summary>Worth fixing.</summary>
    Warning = 1,

    /// <summary>Fix before this reaches anything that matters.</summary>
    Critical = 2,
}
