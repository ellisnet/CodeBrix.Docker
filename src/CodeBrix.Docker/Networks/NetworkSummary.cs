using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A network as returned by <c>GET /networks</c> — the shape behind <c>docker network ls</c>.
/// </summary>
public sealed class NetworkSummary
{
    /// <summary>Gets the network name.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the network id.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets when the network was created.</summary>
    [JsonPropertyName("Created")]
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the scope, for example <c>local</c> or <c>swarm</c>.</summary>
    [JsonPropertyName("Scope")]
    public string Scope { get; init; }

    /// <summary>Gets the driver, for example <c>bridge</c>, <c>host</c> or <c>none</c>.</summary>
    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    /// <summary>Gets a value indicating whether IPv6 is enabled on the network.</summary>
    [JsonPropertyName("EnableIPv6")]
    public bool EnableIPv6 { get; init; }

    /// <summary>Gets a value indicating whether the network is cut off from the outside world.</summary>
    [JsonPropertyName("Internal")]
    public bool Internal { get; init; }

    /// <summary>Gets a value indicating whether standalone containers may attach to the network.</summary>
    [JsonPropertyName("Attachable")]
    public bool Attachable { get; init; }

    /// <summary>Gets a value indicating whether the network is the swarm ingress network.</summary>
    [JsonPropertyName("Ingress")]
    public bool Ingress { get; init; }

    /// <summary>Gets the IP address management configuration.</summary>
    [JsonPropertyName("IPAM")]
    public NetworkIpam Ipam { get; init; }

    /// <summary>Gets the driver options.</summary>
    [JsonPropertyName("Options")]
    public IReadOnlyDictionary<string, string> Options { get; init; }

    /// <summary>Gets the network labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>Gets the id truncated to twelve characters.</summary>
    [JsonIgnore]
    public string ShortId => Id.Length >= 12 ? Id[..12] : Id;

    /// <summary>
    /// Gets a value indicating whether this is one of the three networks the daemon creates and
    /// which cannot be removed.
    /// </summary>
    [JsonIgnore]
    public bool IsPredefined =>
        Name is "bridge" or "host" or "none";
}
