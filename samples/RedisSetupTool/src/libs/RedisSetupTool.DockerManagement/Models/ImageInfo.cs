using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A locally stored image as it appears in a list.</summary>
public sealed class ImageInfo
{
    /// <summary>Gets the image id.</summary>
    public string Id { get; init; }

    /// <summary>Gets a shortened image id.</summary>
    public string ShortId { get; init; }

    /// <summary>Gets the first repository tag, or the short id when the image is untagged.</summary>
    public string DisplayName { get; init; }

    /// <summary>Gets the repository tags; never null.</summary>
    public IReadOnlyList<string> RepoTags { get; init; } = [];

    /// <summary>Gets the repository digests; never null.</summary>
    public IReadOnlyList<string> RepoDigests { get; init; } = [];

    /// <summary>Gets when the image was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the image size, in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the size shared with other images, in bytes.</summary>
    public long SharedSizeBytes { get; init; }

    /// <summary>Gets the image labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets how many containers use the image.</summary>
    public long ContainerCount { get; init; }

    /// <summary>Gets a value indicating whether the image has no usable tag.</summary>
    public bool IsDangling { get; init; }
}
