using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// An image's root filesystem description, from <c>RootFS</c> in the image inspect payload.
/// </summary>
public sealed class ImageRootFs
{
    /// <summary>Gets the root filesystem type, in practice always <c>layers</c>.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; }

    /// <summary>Gets the diff ids of the image's layers, oldest first.</summary>
    [JsonPropertyName("Layers")]
    public IReadOnlyList<string> Layers { get; init; }
}
