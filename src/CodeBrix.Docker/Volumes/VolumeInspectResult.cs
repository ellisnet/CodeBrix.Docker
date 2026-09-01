using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The full description of a volume, from <c>GET /volumes/{name}</c>. It adds usage figures to what
/// <see cref="VolumeSummary"/> reports.
/// </summary>
public sealed class VolumeInspectResult
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

    /// <summary>
    /// Gets the size and reference count, when the daemon computed them. Volume listing and inspect
    /// leave this unset; <c>GET /system/df</c> is what fills it in.
    /// </summary>
    [JsonPropertyName("UsageData")]
    public VolumeUsageData UsageData { get; init; }
}
