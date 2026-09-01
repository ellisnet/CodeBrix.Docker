namespace RedisSetupTool.TerminalView;

/// <summary>Where a console session is in its life.</summary>
public enum TerminalSessionState
{
    /// <summary>Created, not yet pumping.</summary>
    Starting,

    /// <summary>Pumping bytes.</summary>
    Running,

    /// <summary>The shell exited.</summary>
    Exited,

    /// <summary>The pump stopped on an error.</summary>
    Failed,
}
