using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>Request body for <c>POST /networks/create</c>.</summary>
internal sealed class NetworkCreateRequest
{
    [JsonPropertyName("Name")]
    public string Name { get; init; }

    [JsonPropertyName("Driver")]
    public string Driver { get; init; }

    [JsonPropertyName("Labels")]
    public IDictionary<string, string> Labels { get; init; }
}

/// <summary>Response body of <c>POST /networks/create</c>.</summary>
internal sealed class NetworkCreateResponse
{
    [JsonPropertyName("Id")]
    public string Id { get; init; }

    [JsonPropertyName("Warning")]
    public string Warning { get; init; }
}

/// <summary>Request body for <c>POST /networks/{id}/connect</c>.</summary>
internal sealed class NetworkConnectRequest
{
    [JsonPropertyName("Container")]
    public string Container { get; init; }

    [JsonPropertyName("EndpointConfig")]
    public EndpointConfigWire EndpointConfig { get; init; }
}

/// <summary>Request body for <c>POST /networks/{id}/disconnect</c>.</summary>
internal sealed class NetworkDisconnectRequest
{
    [JsonPropertyName("Container")]
    public string Container { get; init; }

    [JsonPropertyName("Force")]
    public bool Force { get; init; }
}

/// <summary>Response body of <c>POST /networks/prune</c>.</summary>
internal sealed class NetworksPruneResponse
{
    [JsonPropertyName("NetworksDeleted")]
    public IReadOnlyList<string> NetworksDeleted { get; init; }
}
