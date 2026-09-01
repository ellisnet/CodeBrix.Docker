using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>
/// The label schema, in one place. Labels are the database: every container, volume and network this
/// tool creates carries them, and discovery rebuilds an instance from nothing else.
/// </summary>
public static class InstanceLabels
{
    /// <summary>The prefix every label shares.</summary>
    public const string Prefix = "codebrix.redissetup.";

    /// <summary>The instance id - the primary key.</summary>
    public const string Instance = Prefix + "instance";

    /// <summary>The topology code, for example <c>D2</c>.</summary>
    public const string Topology = Prefix + "topology";

    /// <summary>The node role, for example <c>primary</c> or <c>cluster-replica</c>.</summary>
    public const string Role = Prefix + "role";

    /// <summary>The one-based node index.</summary>
    public const string Node = Prefix + "node";

    /// <summary>The user's friendly instance name.</summary>
    public const string Name = Prefix + "name";

    /// <summary>When the instance was created, ISO-8601 UTC.</summary>
    public const string Created = Prefix + "created";

    /// <summary>The published host data port.</summary>
    public const string Port = Prefix + "port";

    /// <summary>The published host cluster-bus port; D2 only.</summary>
    public const string BusPort = Prefix + "busport";

    /// <summary>The network gateway the nodes announce, when the topology announces one.</summary>
    public const string AnnounceIp = Prefix + "announceip";

    /// <summary>The image the node runs.</summary>
    public const string Image = Prefix + "image";

    /// <summary>The shared password, when the topology has one.</summary>
    public const string Secret = Prefix + "secret";

    /// <summary>The declared ACL users, as <c>name|password|permissions</c> records.</summary>
    public const string Users = Prefix + "users";

    /// <summary>The sentinel master name; C1 only.</summary>
    public const string Service = Prefix + "service";

    /// <summary>What kind of resource carries the labels: network, volume or container.</summary>
    public const string Resource = Prefix + "resource";

    /// <summary>The separator between the records in the <see cref="Users"/> label.</summary>
    public const string UserRecordSeparator = ";;";

    /// <summary>
    /// Gets a filter matching every resource this tool created, whatever its instance. A null value
    /// makes the query builder emit a presence match rather than an equality match.
    /// </summary>
    public static IDictionary<string, string> PresenceFilter =>
        new Dictionary<string, string>(StringComparer.Ordinal) { [Instance] = null };

    /// <summary>Gets a filter matching one instance's resources.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <returns>A fresh filter dictionary.</returns>
    public static IDictionary<string, string> InstanceFilter(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new Dictionary<string, string>(StringComparer.Ordinal) { [Instance] = instanceId };
    }

    /// <summary>Reads one label, returning null when it is absent.</summary>
    /// <param name="labels">The label set, which may be null.</param>
    /// <param name="name">The label name.</param>
    /// <returns>The value, or null.</returns>
    public static string Read(IReadOnlyDictionary<string, string> labels, string name) =>
        labels is not null && labels.TryGetValue(name, out var value) ? value : null;
}
