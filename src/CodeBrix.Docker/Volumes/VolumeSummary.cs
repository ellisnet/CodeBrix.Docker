using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A volume as returned in the <c>Volumes</c> array of <c>GET /volumes</c> — the shape behind
/// <c>docker volume ls</c>.
/// </summary>
public sealed class VolumeSummary
{
    /// <summary>Gets the volume name, which is also its identifier.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the volume driver, in practice <c>local</c>.</summary>
    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    /// <summary>Gets the path on the host where the volume's data lives.</summary>
    [JsonPropertyName("Mountpoint")]
    public string Mountpoint { get; init; }

    /// <summary>Gets when the volume was created.</summary>
    [JsonPropertyName("CreatedAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Gets the volume labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>Gets the driver options the volume was created with.</summary>
    [JsonPropertyName("Options")]
    public IReadOnlyDictionary<string, string> Options { get; init; }

    /// <summary>Gets the scope, for example <c>local</c>.</summary>
    [JsonPropertyName("Scope")]
    public string Scope { get; init; }
}
