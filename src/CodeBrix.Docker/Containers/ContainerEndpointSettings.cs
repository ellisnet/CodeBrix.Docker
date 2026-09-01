using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A container's attachment to one network.
/// </summary>
public sealed class ContainerEndpointSettings
{
    /// <summary>Gets the network id.</summary>
    [JsonPropertyName("NetworkID")]
    public string NetworkId { get; init; }

    /// <summary>Gets the endpoint id.</summary>
    [JsonPropertyName("EndpointID")]
    public string EndpointId { get; init; }

    /// <summary>Gets the container's IPv4 address on this network.</summary>
    [JsonPropertyName("IPAddress")]
    public string IpAddress { get; init; }

    /// <summary>Gets the IPv4 prefix length.</summary>
    [JsonPropertyName("IPPrefixLen")]
    public int IpPrefixLength { get; init; }

    /// <summary>Gets the default gateway on this network.</summary>
    [JsonPropertyName("Gateway")]
    public string Gateway { get; init; }

    /// <summary>Gets the MAC address on this network.</summary>
    [JsonPropertyName("MacAddress")]
    public string MacAddress { get; init; }

    /// <summary>Gets the extra DNS names the container answers to on this network.</summary>
    [JsonPropertyName("Aliases")]
    public IReadOnlyList<string> Aliases { get; init; }
}
