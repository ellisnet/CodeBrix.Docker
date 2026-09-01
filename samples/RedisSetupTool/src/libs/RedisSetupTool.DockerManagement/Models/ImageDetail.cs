using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>Everything an inspect returns about one image.</summary>
public sealed class ImageDetail
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

    /// <summary>Gets the parent image id.</summary>
    public string Parent { get; init; }

    /// <summary>Gets the image comment.</summary>
    public string Comment { get; init; }

    /// <summary>Gets when the image was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the image author.</summary>
    public string Author { get; init; }

    /// <summary>Gets the target architecture.</summary>
    public string Architecture { get; init; }

    /// <summary>Gets the target operating system.</summary>
    public string Os { get; init; }

    /// <summary>Gets the image size, in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the number of filesystem layers.</summary>
    public int LayerCount { get; init; }

    /// <summary>Gets the layer digests; never null.</summary>
    public IReadOnlyList<string> Layers { get; init; } = [];

    /// <summary>Gets the configured environment; never null.</summary>
    public IReadOnlyList<string> Env { get; init; } = [];

    /// <summary>Gets the configured command; never null.</summary>
    public IReadOnlyList<string> Cmd { get; init; } = [];

    /// <summary>Gets the configured entrypoint; never null.</summary>
    public IReadOnlyList<string> Entrypoint { get; init; } = [];

    /// <summary>Gets the configured working directory.</summary>
    public string WorkingDir { get; init; }

    /// <summary>Gets the configured user.</summary>
    public string User { get; init; }

    /// <summary>Gets the image labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
