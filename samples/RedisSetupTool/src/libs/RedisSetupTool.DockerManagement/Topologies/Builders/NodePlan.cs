using System.Collections.Generic;
using CodeBrix.Docker;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>What one container of an instance should be. Internal: it names CodeBrix.Docker's limits type.</summary>
internal sealed class NodePlan
{
    /// <summary>Gets or sets the role name used for the container name and the network alias.</summary>
    internal string RoleName { get; set; }

    /// <summary>Gets or sets the value of the role label.</summary>
    internal string RoleLabel { get; set; }

    /// <summary>Gets or sets what the node does.</summary>
    internal NodeRole Role { get; set; }

    /// <summary>Gets or sets the one-based node index.</summary>
    internal int NodeIndex { get; set; }

    /// <summary>Gets or sets the port inside the container.</summary>
    internal int ContainerPort { get; set; }

    /// <summary>Gets or sets the published host port.</summary>
    internal int HostPort { get; set; }

    /// <summary>Gets or sets the published cluster-bus port.</summary>
    internal int? BusHostPort { get; set; }

    /// <summary>Gets or sets the container command.</summary>
    internal IReadOnlyList<string> Command { get; set; }

    /// <summary>Gets or sets the entrypoint override; null everywhere except the sentinels.</summary>
    internal IReadOnlyList<string> Entrypoint { get; set; }

    /// <summary>Gets or sets the container resource limits; null everywhere except G1.</summary>
    internal ResourceLimits Limits { get; set; }
}
