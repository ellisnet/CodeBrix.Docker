namespace CodeBrix.Docker;

/// <summary>
/// The kinds of mount a container can receive.
/// </summary>
public enum MountKind
{
    /// <summary>A Docker-managed named volume.</summary>
    Volume,

    /// <summary>A bind mount of a host path.</summary>
    Bind,

    /// <summary>An in-memory tmpfs filesystem.</summary>
    Tmpfs,
}
