using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A mount as the daemon reports it on a created container.
/// </summary>
public sealed class ContainerMountPoint
{
    /// <summary>Gets the mount type: <c>volume</c>, <c>bind</c> or <c>tmpfs</c>.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; }

    /// <summary>Gets the volume name, for volume mounts.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    /// <summary>Gets the host-side path backing the mount.</summary>
    [JsonPropertyName("Source")]
    public string Source { get; init; }

    /// <summary>Gets the path inside the container.</summary>
    [JsonPropertyName("Destination")]
    public string Destination { get; init; }

    /// <summary>Gets the volume driver, for volume mounts.</summary>
    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    /// <summary>Gets the mount mode string.</summary>
    [JsonPropertyName("Mode")]
    public string Mode { get; init; }

    /// <summary>Gets a value indicating whether the mount is writable.</summary>
    [JsonPropertyName("RW")]
    public bool ReadWrite { get; init; }
}
