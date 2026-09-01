namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One Dockerfile lint finding.</summary>
public sealed class LintFindingInfo
{
    /// <summary>Gets the rule code, for example <c>DL3008</c>.</summary>
    public string Code { get; init; }

    /// <summary>Gets the severity word.</summary>
    public string Level { get; init; }

    /// <summary>Gets the one-based line number.</summary>
    public int Line { get; init; }

    /// <summary>Gets the one-based column number.</summary>
    public int Column { get; init; }

    /// <summary>Gets the message.</summary>
    public string Message { get; init; }
}
