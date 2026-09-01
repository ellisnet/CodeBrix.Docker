namespace CodeBrix.Docker;

/// <summary>
/// What the daemon should do when a container exits.
/// </summary>
public enum RestartPolicyKind
{
    /// <summary>Never restart the container automatically.</summary>
    No,

    /// <summary>Always restart, including after a daemon restart.</summary>
    Always,

    /// <summary>Restart only when the container exits non-zero.</summary>
    OnFailure,

    /// <summary>Always restart, except when the container was stopped explicitly.</summary>
    UnlessStopped,
}
