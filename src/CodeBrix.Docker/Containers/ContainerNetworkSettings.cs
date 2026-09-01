using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A container's network attachments, from <c>NetworkSettings</c> in the inspect payload.
/// </summary>
public sealed class ContainerNetworkSettings
{
    /// <summary>Gets the networks the container is attached to, keyed by network name.</summary>
    [JsonPropertyName("Networks")]
    public IReadOnlyDictionary<string, ContainerEndpointSettings> Networks { get; init; }

    /// <summary>Gets the container's address on the default bridge network, when it has one.</summary>
    [JsonPropertyName("IPAddress")]
    public string IpAddress { get; init; }
}
