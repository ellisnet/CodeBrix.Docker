using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A container as it appears in a list.</summary>
public sealed class ContainerInfo
{
    /// <summary>Gets the full container id.</summary>
    public string Id { get; init; }

    /// <summary>Gets the first twelve characters of the id.</summary>
    public string ShortId { get; init; }

    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string Name { get; init; }

    /// <summary>Gets the image reference the container runs.</summary>
    public string Image { get; init; }

    /// <summary>Gets the image id the container runs.</summary>
    public string ImageId { get; init; }

    /// <summary>Gets the command line the container was started with.</summary>
    public string Command { get; init; }

    /// <summary>Gets when the container was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the state word, for example <c>running</c>.</summary>
    public string State { get; init; }

    /// <summary>Gets the human status, for example <c>Up 3 minutes</c>.</summary>
    public string Status { get; init; }

    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the container labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the port mappings; never null.</summary>
    public IReadOnlyList<PortMapping> Ports { get; init; } = [];

    /// <summary>Gets the writable-layer size, in bytes.</summary>
    public long SizeRwBytes { get; init; }

    /// <summary>Gets the total on-disk size, in bytes.</summary>
    public long SizeRootFsBytes { get; init; }

    /// <summary>Gets the RedisSetupTool instance id, when the container carries one.</summary>
    public string InstanceId { get; init; }

    /// <summary>Gets the topology code, when the container carries one.</summary>
    public string TopologyCode { get; init; }

    /// <summary>Gets the node role, when the container carries one.</summary>
    public string Role { get; init; }

    /// <summary>Gets the one-based node index, when the container carries one.</summary>
    public int? NodeIndex { get; init; }

    /// <summary>Gets a value indicating whether this tool created the container.</summary>
    public bool IsManaged { get; init; }
}
