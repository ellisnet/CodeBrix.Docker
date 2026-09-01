namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One mount inside a container.</summary>
public sealed class MountInfo
{
    /// <summary>Gets the mount type, for example <c>volume</c> or <c>bind</c>.</summary>
    public string Type { get; init; }

    /// <summary>Gets the volume name, for a volume mount.</summary>
    public string Name { get; init; }

    /// <summary>Gets the host-side source path.</summary>
    public string Source { get; init; }

    /// <summary>Gets the path inside the container.</summary>
    public string Destination { get; init; }

    /// <summary>Gets a value indicating whether the mount is writable.</summary>
    public bool ReadWrite { get; init; }
}
