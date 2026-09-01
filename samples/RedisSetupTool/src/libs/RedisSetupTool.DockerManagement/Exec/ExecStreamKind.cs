namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>Which stream a read came from.</summary>
public enum ExecStreamKind
{
    /// <summary>Nothing was read.</summary>
    None,

    /// <summary>Standard output - and, with a terminal, everything.</summary>
    StandardOutput,

    /// <summary>Standard error, which only exists when no terminal was allocated.</summary>
    StandardError,
}
