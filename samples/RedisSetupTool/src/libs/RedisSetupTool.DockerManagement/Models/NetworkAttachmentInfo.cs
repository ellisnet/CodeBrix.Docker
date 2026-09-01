namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One container attached to a network.</summary>
public sealed class NetworkAttachmentInfo
{
    /// <summary>Gets the container name.</summary>
    public string ContainerName { get; init; }

    /// <summary>Gets the endpoint id.</summary>
    public string EndpointId { get; init; }

    /// <summary>Gets the container's address on the network.</summary>
    public string IPv4Address { get; init; }

    /// <summary>Gets the container's MAC address on the network.</summary>
    public string MacAddress { get; init; }
}
