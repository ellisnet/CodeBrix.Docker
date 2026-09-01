using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One network a container is attached to.</summary>
public sealed class ContainerNetworkAttachment
{
    /// <summary>Gets the network name.</summary>
    public string NetworkName { get; init; }

    /// <summary>Gets the network id.</summary>
    public string NetworkId { get; init; }

    /// <summary>Gets the address the container holds on the network.</summary>
    public string IpAddress { get; init; }

    /// <summary>Gets the network gateway address.</summary>
    public string Gateway { get; init; }

    /// <summary>Gets the container's MAC address on the network.</summary>
    public string MacAddress { get; init; }

    /// <summary>Gets the network aliases; never null.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
