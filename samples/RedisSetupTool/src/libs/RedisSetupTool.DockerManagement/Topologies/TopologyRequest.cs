using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>What the user asked to create.</summary>
public sealed class TopologyRequest
{
    /// <summary>Gets or sets the topology to create.</summary>
    public TopologyId TopologyId { get; set; }

    /// <summary>Gets or sets the friendly instance name; blank falls back to the topology code.</summary>
    public string InstanceName { get; set; }

    /// <summary>Gets the parameter values, keyed by <see cref="TopologyParameter.Key"/>.</summary>
    public IDictionary<string, string> Parameters { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the extra labels every resource created for the request carries.</summary>
    /// <remarks>The test fixture uses this to tag - and only then sweep - its own resources.</remarks>
    public IDictionary<string, string> ExtraLabels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
