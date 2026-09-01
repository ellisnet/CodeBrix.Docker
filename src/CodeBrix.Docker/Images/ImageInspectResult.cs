using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The full description of an image, from <c>GET /images/{name}/json</c>.
/// </summary>
public sealed class ImageInspectResult
{
    /// <summary>Gets the image id, including the <c>sha256:</c> prefix.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the tags pointing at this image.</summary>
    [JsonPropertyName("RepoTags")]
    public IReadOnlyList<string> RepoTags { get; init; }

    /// <summary>Gets the registry digests this image is known by.</summary>
    [JsonPropertyName("RepoDigests")]
    public IReadOnlyList<string> RepoDigests { get; init; }

    /// <summary>Gets the parent image id, for images built by the legacy builder.</summary>
    [JsonPropertyName("Parent")]
    public string Parent { get; init; }

    /// <summary>Gets the image comment.</summary>
    [JsonPropertyName("Comment")]
    public string Comment { get; init; }

    /// <summary>Gets when the image was created.</summary>
    [JsonPropertyName("Created")]
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the image author.</summary>
    [JsonPropertyName("Author")]
    public string Author { get; init; }

    /// <summary>Gets the CPU architecture the image was built for, for example <c>amd64</c> or <c>arm64</c>.</summary>
    [JsonPropertyName("Architecture")]
    public string Architecture { get; init; }

    /// <summary>Gets the operating system the image targets, for example <c>linux</c>.</summary>
    [JsonPropertyName("Os")]
    public string Os { get; init; }

    /// <summary>Gets the total size of the image's layers, in bytes.</summary>
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    /// <summary>Gets the configuration baked into the image.</summary>
    [JsonPropertyName("Config")]
    public ImageConfig Config { get; init; }

    /// <summary>Gets the root filesystem description, whose layer count indicates build efficiency.</summary>
    [JsonPropertyName("RootFS")]
    public ImageRootFs RootFs { get; init; }

    /// <summary>Gets the number of layers in the image.</summary>
    [JsonIgnore]
    public int LayerCount => RootFs?.Layers?.Count ?? 0;

    /// <summary>Gets the first tag pointing at this image, or the short id when it is untagged.</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (RepoTags is not null)
            {
                foreach (var tag in RepoTags)
                {
                    if (!string.IsNullOrEmpty(tag) && !tag.StartsWith("<none>", StringComparison.Ordinal))
                    {
                        return tag;
                    }
                }
            }

            return ShortId;
        }
    }

    /// <summary>Gets the id without its algorithm prefix, truncated to twelve characters.</summary>
    [JsonIgnore]
    public string ShortId
    {
        get
        {
            var id = Id;
            var colon = id.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                id = id[(colon + 1)..];
            }

            return id.Length >= 12 ? id[..12] : id;
        }
    }
}
