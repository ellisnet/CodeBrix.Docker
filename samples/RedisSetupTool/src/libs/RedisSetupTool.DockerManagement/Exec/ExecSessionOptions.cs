using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>How to open an interactive shell.</summary>
public sealed class ExecSessionOptions
{
    /// <summary>Gets or sets the shells to try, in order; null uses the default list.</summary>
    public IReadOnlyList<string> ShellCandidates { get; set; }

    /// <summary>Gets or sets the initial terminal row count.</summary>
    public int Rows { get; set; } = 24;

    /// <summary>Gets or sets the initial terminal column count.</summary>
    public int Columns { get; set; } = 80;

    /// <summary>Gets or sets the environment handed to the shell.</summary>
    public IReadOnlyList<string> Env { get; set; } =
        ["TERM=xterm-256color", "PS1=\\h:\\w$ ", "LANG=C.UTF-8"];

    /// <summary>Gets or sets the user to run as.</summary>
    public string User { get; set; }

    /// <summary>Gets or sets the working directory.</summary>
    public string WorkingDir { get; set; }
}
