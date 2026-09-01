using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One entry from an image's build history.</summary>
public sealed class ImageLayerInfo
{
    /// <summary>Gets the layer id, when the daemon knows one.</summary>
    public string Id { get; init; }

    /// <summary>Gets when the layer was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the build instruction that produced the layer.</summary>
    public string CreatedBy { get; init; }

    /// <summary>Gets the layer size, in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the layer comment.</summary>
    public string Comment { get; init; }

    /// <summary>Gets the tags pointing at the layer; never null.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets a value indicating whether the layer adds no bytes.</summary>
    public bool IsEmptyLayer { get; init; }
}
