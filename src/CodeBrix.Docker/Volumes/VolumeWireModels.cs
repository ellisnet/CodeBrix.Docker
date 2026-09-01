using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>Request body for <c>POST /volumes/create</c>.</summary>
internal sealed class VolumeCreateRequest
{
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    [JsonPropertyName("Labels")]
    public IDictionary<string, string> Labels { get; init; }
}

/// <summary>Response body of <c>GET /volumes</c>, which wraps the array in an object.</summary>
internal sealed class VolumeListResponse
{
    [JsonPropertyName("Volumes")]
    public List<VolumeSummary> Volumes { get; init; }

    [JsonPropertyName("Warnings")]
    public IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Response body of <c>POST /volumes/prune</c>.</summary>
internal sealed class VolumesPruneResponse
{
    [JsonPropertyName("VolumesDeleted")]
    public IReadOnlyList<string> VolumesDeleted { get; init; }

    [JsonPropertyName("SpaceReclaimed")]
    public long SpaceReclaimed { get; init; }
}
