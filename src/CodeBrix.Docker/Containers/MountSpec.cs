using System;

namespace CodeBrix.Docker;

/// <summary>
/// A mount to attach to a container. Create instances with <see cref="Volume"/>, <see cref="Bind"/>
/// or <see cref="Tmpfs"/>.
/// </summary>
public sealed class MountSpec
{
    private MountSpec(MountKind kind, string source, string target, bool readOnly, long? tmpfsSizeBytes)
    {
        Kind = kind;
        Source = source;
        Target = target;
        ReadOnly = readOnly;
        TmpfsSizeBytes = tmpfsSizeBytes;
    }

    /// <summary>Gets the mount kind.</summary>
    public MountKind Kind { get; }

    /// <summary>
    /// Gets the source: a volume name for <see cref="MountKind.Volume"/>, a host path for
    /// <see cref="MountKind.Bind"/>, and <see langword="null"/> for <see cref="MountKind.Tmpfs"/>.
    /// </summary>
    public string Source { get; }

    /// <summary>Gets the path inside the container.</summary>
    public string Target { get; }

    /// <summary>Gets a value indicating whether the mount is read-only.</summary>
    public bool ReadOnly { get; }

    /// <summary>Gets the tmpfs size limit in bytes, when one was requested.</summary>
    public long? TmpfsSizeBytes { get; }

    /// <summary>
    /// Mounts a Docker-managed named volume. The volume is created on demand if it does not exist.
    /// </summary>
    /// <param name="name">The volume name.</param>
    /// <param name="containerPath">The path inside the container.</param>
    /// <param name="readOnly">Whether the mount is read-only.</param>
    /// <returns>The mount specification.</returns>
    public static MountSpec Volume(string name, string containerPath, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        return new MountSpec(MountKind.Volume, name, containerPath, readOnly, null);
    }

    /// <summary>
    /// Bind-mounts a host path into the container.
    /// </summary>
    /// <param name="hostPath">The path on the host.</param>
    /// <param name="containerPath">The path inside the container.</param>
    /// <param name="readOnly">Whether the mount is read-only.</param>
    /// <returns>The mount specification.</returns>
    public static MountSpec Bind(string hostPath, string containerPath, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        return new MountSpec(MountKind.Bind, hostPath, containerPath, readOnly, null);
    }

    /// <summary>
    /// Mounts an in-memory tmpfs filesystem, which keeps scratch writes out of the container's
    /// copy-on-write layer.
    /// </summary>
    /// <param name="containerPath">The path inside the container.</param>
    /// <param name="sizeBytes">An optional size limit in bytes.</param>
    /// <returns>The mount specification.</returns>
    public static MountSpec Tmpfs(string containerPath, long? sizeBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        return new MountSpec(MountKind.Tmpfs, null, containerPath, false, sizeBytes);
    }

    /// <summary>Gets the daemon's wire name for <see cref="Kind"/>.</summary>
    internal string TypeName => Kind switch
    {
        MountKind.Volume => "volume",
        MountKind.Bind => "bind",
        MountKind.Tmpfs => "tmpfs",
        _ => throw new DockerException($"Unsupported mount kind '{Kind}'."),
    };
}
