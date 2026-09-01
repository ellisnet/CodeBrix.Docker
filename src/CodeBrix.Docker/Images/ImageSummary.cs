using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// An image as returned by <c>GET /images/json</c> — the shape behind <c>docker images</c>.
/// </summary>
public sealed class ImageSummary
{
    /// <summary>Gets the image id, including the <c>sha256:</c> prefix.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the id of the parent image, when the daemon tracks one.</summary>
    [JsonPropertyName("ParentId")]
    public string ParentId { get; init; }

    /// <summary>
    /// Gets the tags pointing at this image. An untagged (dangling) image reports
    /// <c>&lt;none&gt;:&lt;none&gt;</c> or nothing at all.
    /// </summary>
    [JsonPropertyName("RepoTags")]
    public IReadOnlyList<string> RepoTags { get; init; }

    /// <summary>Gets the registry digests this image is known by.</summary>
    [JsonPropertyName("RepoDigests")]
    public IReadOnlyList<string> RepoDigests { get; init; }

    /// <summary>Gets the creation time as Unix seconds, as the daemon reports it on this endpoint.</summary>
    [JsonPropertyName("Created")]
    public long CreatedUnixSeconds { get; init; }

    /// <summary>Gets the total size of the image's layers, in bytes.</summary>
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    /// <summary>
    /// Gets the size of the layers this image shares with other images, in bytes, or <c>-1</c> when
    /// the daemon did not compute it.
    /// </summary>
    [JsonPropertyName("SharedSize")]
    public long SharedSize { get; init; }

    /// <summary>Gets the image labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>
    /// Gets the number of containers created from this image, or <c>-1</c> when the daemon did not
    /// count them.
    /// </summary>
    [JsonPropertyName("Containers")]
    public long Containers { get; init; }

    /// <summary>Gets the creation time, or <see langword="null"/> when the daemon reported none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Created =>
        CreatedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(CreatedUnixSeconds) : null;

    /// <summary>Gets the first usable tag, or the short id when the image is untagged.</summary>
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

    /// <summary>Gets a value indicating whether the image carries no usable tag.</summary>
    [JsonIgnore]
    public bool IsDangling =>
        RepoTags is null
        || RepoTags.Count == 0
        || RepoTags.All(tag => string.IsNullOrEmpty(tag) || tag.StartsWith("<none>", StringComparison.Ordinal));
}
