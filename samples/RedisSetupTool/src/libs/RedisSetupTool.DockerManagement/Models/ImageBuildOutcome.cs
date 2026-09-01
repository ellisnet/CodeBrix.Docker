using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The result of a build.</summary>
public sealed class ImageBuildOutcome
{
    /// <summary>Gets the id of the image that was built.</summary>
    public string ImageId { get; init; }

    /// <summary>Gets a shortened image id.</summary>
    public string ShortImageId { get; init; }

    /// <summary>Gets the tags applied; never null.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the builder's transcript.</summary>
    public string Output { get; init; } = string.Empty;
}
