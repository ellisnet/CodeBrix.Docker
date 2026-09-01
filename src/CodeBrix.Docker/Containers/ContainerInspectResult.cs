using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The full description of a container, from <c>GET /containers/{id}/json</c>.
/// </summary>
public sealed class ContainerInspectResult
{
    /// <summary>Gets the full container id.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the container name, including the daemon's leading slash.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    /// <summary>Gets when the container was created.</summary>
    [JsonPropertyName("Created")]
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the image id the container runs.</summary>
    [JsonPropertyName("Image")]
    public string Image { get; init; }

    /// <summary>Gets the path to the container's log file on the host, for file-based log drivers.</summary>
    [JsonPropertyName("LogPath")]
    public string LogPath { get; init; }

    /// <summary>Gets how many times the daemon has restarted the container.</summary>
    [JsonPropertyName("RestartCount")]
    public long RestartCount { get; init; }

    /// <summary>Gets the runtime state, including OOM and health information.</summary>
    [JsonPropertyName("State")]
    public ContainerState State { get; init; }

    /// <summary>Gets the image-level configuration.</summary>
    [JsonPropertyName("Config")]
    public ContainerConfig Config { get; init; }

    /// <summary>Gets the host-side configuration, including all resource limits.</summary>
    [JsonPropertyName("HostConfig")]
    public ContainerHostConfig HostConfig { get; init; }

    /// <summary>Gets the network attachments.</summary>
    [JsonPropertyName("NetworkSettings")]
    public ContainerNetworkSettings NetworkSettings { get; init; }

    /// <summary>Gets the mounts attached to the container.</summary>
    [JsonPropertyName("Mounts")]
    public IReadOnlyList<ContainerMountPoint> Mounts { get; init; }

    /// <summary>Gets the container name without the daemon's leading slash.</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(Name))
            {
                return Name.TrimStart('/');
            }

            return Id.Length >= 12 ? Id[..12] : Id;
        }
    }

    /// <summary>Gets a value indicating whether the container is currently running.</summary>
    [JsonIgnore]
    public bool IsRunning => State?.Running == true;
}
