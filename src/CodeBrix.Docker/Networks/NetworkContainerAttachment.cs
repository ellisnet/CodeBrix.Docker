using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// One container's attachment to a network, from the <c>Containers</c> map of a network inspect
/// payload.
/// </summary>
public sealed class NetworkContainerAttachment
{
    /// <summary>Gets the container name.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    /// <summary>Gets the endpoint id.</summary>
    [JsonPropertyName("EndpointID")]
    public string EndpointId { get; init; }

    /// <summary>Gets the MAC address the container uses on this network.</summary>
    [JsonPropertyName("MacAddress")]
    public string MacAddress { get; init; }

    /// <summary>Gets the container's IPv4 address on this network, in CIDR form.</summary>
    [JsonPropertyName("IPv4Address")]
    public string IPv4Address { get; init; }

    /// <summary>Gets the container's IPv6 address on this network, in CIDR form.</summary>
    [JsonPropertyName("IPv6Address")]
    public string IPv6Address { get; init; }
}
