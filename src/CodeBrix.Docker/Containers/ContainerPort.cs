using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A port exposed or published by a container, as reported by the container list endpoint.
/// </summary>
public sealed class ContainerPort
{
    /// <summary>Gets the host IP the port is published on, when it is published.</summary>
    [JsonPropertyName("IP")]
    public string Ip { get; init; }

    /// <summary>Gets the host port, when the port is published.</summary>
    [JsonPropertyName("PublicPort")]
    public int? PublicPort { get; init; }

    /// <summary>Gets the port inside the container.</summary>
    [JsonPropertyName("PrivatePort")]
    public int PrivatePort { get; init; }

    /// <summary>Gets the protocol, <c>tcp</c> or <c>udp</c>.</summary>
    [JsonPropertyName("Type")]
    public string Protocol { get; init; }
}
