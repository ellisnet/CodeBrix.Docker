using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a successful <see cref="ImageOperations.BuildAsync"/> call.
/// </summary>
public sealed class ImageBuildResult
{
    /// <summary>
    /// Gets the id of the built image, including the <c>sha256:</c> prefix. Empty when the id could
    /// not be resolved — for example when the build ran on a builder that does not load its result
    /// into the local image store.
    /// </summary>
    public string ImageId { get; init; } = string.Empty;

    /// <summary>Gets the tags applied to the built image.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Gets the combined build log, standard output and standard error interleaved in arrival order.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Gets the id without its algorithm prefix, truncated to twelve characters.</summary>
    public string ShortImageId
    {
        get
        {
            var id = ImageId;
            var colon = id.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                id = id[(colon + 1)..];
            }

            return id.Length >= 12 ? id[..12] : id;
        }
    }
}
