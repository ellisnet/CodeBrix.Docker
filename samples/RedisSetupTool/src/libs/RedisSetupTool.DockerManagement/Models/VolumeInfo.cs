using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A Docker volume.</summary>
public sealed class VolumeInfo
{
    /// <summary>Gets the volume name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the driver, normally <c>local</c>.</summary>
    public string Driver { get; init; }

    /// <summary>Gets the host path the volume lives at.</summary>
    public string Mountpoint { get; init; }

    /// <summary>Gets when the volume was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets the volume labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the scope, normally <c>local</c>.</summary>
    public string Scope { get; init; }

    /// <summary>Gets the volume size, when the daemon computed one.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Gets how many containers reference the volume, when the daemon computed it.</summary>
    public long? RefCount { get; init; }

    /// <summary>Gets the RedisSetupTool instance id, when the volume carries one.</summary>
    public string InstanceId { get; init; }
}
