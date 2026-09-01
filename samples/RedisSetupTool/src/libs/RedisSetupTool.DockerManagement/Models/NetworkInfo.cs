using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A Docker network.</summary>
public sealed class NetworkInfo
{
    /// <summary>Gets the network id.</summary>
    public string Id { get; init; }

    /// <summary>Gets a shortened network id.</summary>
    public string ShortId { get; init; }

    /// <summary>Gets the network name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the driver, normally <c>bridge</c>.</summary>
    public string Driver { get; init; }

    /// <summary>Gets the scope, normally <c>local</c>.</summary>
    public string Scope { get; init; }

    /// <summary>Gets when the network was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets a value indicating whether the network is internal.</summary>
    public bool IsInternal { get; init; }

    /// <summary>Gets a value indicating whether containers may attach at run time.</summary>
    public bool IsAttachable { get; init; }

    /// <summary>Gets a value indicating whether the network is the swarm ingress network.</summary>
    public bool IsIngress { get; init; }

    /// <summary>Gets a value indicating whether the network is one Docker creates for itself.</summary>
    public bool IsPredefined { get; init; }

    /// <summary>Gets the network labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the first configured subnet.</summary>
    public string Subnet { get; init; }

    /// <summary>Gets the first configured gateway - the announce address the topologies use.</summary>
    public string Gateway { get; init; }

    /// <summary>Gets how many containers are attached.</summary>
    public int AttachedContainerCount { get; init; }

    /// <summary>Gets the attachments; never null.</summary>
    public IReadOnlyList<NetworkAttachmentInfo> AttachedContainers { get; init; } = [];

    /// <summary>Gets the RedisSetupTool instance id, when the network carries one.</summary>
    public string InstanceId { get; init; }
}
