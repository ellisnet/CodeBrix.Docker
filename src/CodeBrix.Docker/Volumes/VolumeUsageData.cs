using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A volume's usage figures. The daemon computes these only on request, reporting <c>-1</c>
/// otherwise.
/// </summary>
public sealed class VolumeUsageData
{
    /// <summary>Gets the size of the volume's data in bytes, or <c>-1</c> when it was not computed.</summary>
    [JsonPropertyName("Size")]
    public long Size { get; init; }

    /// <summary>
    /// Gets the number of containers using the volume, or <c>-1</c> when it was not computed.
    /// </summary>
    [JsonPropertyName("RefCount")]
    public long RefCount { get; init; }

    /// <summary>Gets a value indicating whether the daemon computed these figures.</summary>
    [JsonIgnore]
    public bool IsComputed => Size >= 0 || RefCount >= 0;
}
