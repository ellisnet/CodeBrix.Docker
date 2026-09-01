using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// One layer in an image's build history, from <c>GET /images/{name}/history</c>.
/// </summary>
/// <remarks>
/// The daemon returns the newest layer first. Layers whose id is <c>&lt;missing&gt;</c> came from a
/// pulled image whose intermediate configurations are not stored locally.
/// </remarks>
public sealed class ImageHistoryEntry
{
    /// <summary>Gets the layer id, or <c>&lt;missing&gt;</c> when the daemon does not have one.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; }

    /// <summary>Gets the creation time as Unix seconds, as the daemon reports it on this endpoint.</summary>
    [JsonPropertyName("Created")]
    public long CreatedUnixSeconds { get; init; }

    /// <summary>Gets the Dockerfile instruction that produced the layer.</summary>
    [JsonPropertyName("CreatedBy")]
    public string CreatedBy { get; init; }

    /// <summary>Gets the tags pointing at this layer, when it is itself a tagged image.</summary>
    [JsonPropertyName("Tags")]
    public IReadOnlyList<string> Tags { get; init; }

    /// <summary>Gets the size the layer adds, in bytes. Zero for metadata-only instructions.</summary>
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    /// <summary>Gets the layer comment.</summary>
    [JsonPropertyName("Comment")]
    public string Comment { get; init; }

    /// <summary>Gets the creation time, or <see langword="null"/> when the daemon reported none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Created =>
        CreatedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(CreatedUnixSeconds) : null;

    /// <summary>Gets a value indicating whether the layer adds no bytes to the image.</summary>
    [JsonIgnore]
    public bool IsEmptyLayer => Size == 0;
}
