using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// One address pool of a network's IP address management configuration.
/// </summary>
public sealed class NetworkIpamConfig
{
    /// <summary>Gets the subnet in CIDR form, for example <c>172.18.0.0/16</c>.</summary>
    [JsonPropertyName("Subnet")]
    public string Subnet { get; init; }

    /// <summary>Gets the range within the subnet that addresses are allocated from.</summary>
    [JsonPropertyName("IPRange")]
    public string IpRange { get; init; }

    /// <summary>Gets the gateway address.</summary>
    [JsonPropertyName("Gateway")]
    public string Gateway { get; init; }
}
