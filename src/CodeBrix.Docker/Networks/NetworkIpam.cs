using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A network's IP address management configuration.
/// </summary>
public sealed class NetworkIpam
{
    /// <summary>Gets the IPAM driver, in practice <c>default</c>.</summary>
    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    /// <summary>Gets the driver options.</summary>
    [JsonPropertyName("Options")]
    public IReadOnlyDictionary<string, string> Options { get; init; }

    /// <summary>Gets the address pools the network allocates from.</summary>
    [JsonPropertyName("Config")]
    public IReadOnlyList<NetworkIpamConfig> Config { get; init; }
}
