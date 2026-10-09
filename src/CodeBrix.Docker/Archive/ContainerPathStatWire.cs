using System;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The JSON carried, base64-encoded, in the <c>X-Docker-Container-Path-Stat</c> response header of
/// the Engine API's archive endpoints.
/// </summary>
internal sealed class ContainerPathStatWire
{
    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("mode")]
    public uint Mode { get; init; }

    [JsonPropertyName("mtime")]
    public DateTimeOffset? Modified { get; init; }

    [JsonPropertyName("linkTarget")]
    public string LinkTarget { get; init; }
}
