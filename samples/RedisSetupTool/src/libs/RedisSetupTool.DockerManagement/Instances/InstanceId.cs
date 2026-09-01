using System;
using System.Globalization;
using System.Security.Cryptography;
using RedisSetupTool.DockerManagement.Topologies;

namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>
/// Instance ids and the resource names derived from them. Every name a topology creates comes from
/// here, so teardown can find everything from the id alone.
/// </summary>
public static class InstanceId
{
    /// <summary>The prefix every container, volume and network name carries.</summary>
    public const string ResourcePrefix = "redissetup-";

    /// <summary>Creates a new instance id of the form <c>d2-1a2b3c4d</c>.</summary>
    /// <param name="topologyId">The topology the instance runs.</param>
    /// <returns>The new id.</returns>
    public static string Create(TopologyId topologyId) =>
        topologyId.ToString().ToLowerInvariant() + "-" + RandomHex(8);

    /// <summary>Generates a lowercase hexadecimal string.</summary>
    /// <param name="characters">How many characters to generate.</param>
    /// <returns>The generated string.</returns>
    public static string RandomHex(int characters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(characters);
        var bytes = RandomNumberGenerator.GetBytes((characters + 1) / 2);
        return Convert.ToHexStringLower(bytes)[..characters];
    }

    /// <summary>Reads the topology out of an instance id.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="topologyId">The topology the id names.</param>
    /// <returns>True when the id carries a known topology code.</returns>
    public static bool TryParseTopology(string instanceId, out TopologyId topologyId)
    {
        topologyId = default;
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var dash = instanceId.IndexOf('-');
        var code = dash < 0 ? instanceId : instanceId[..dash];
        return TopologyCatalog.TryParseCode(code, out topologyId);
    }

    /// <summary>Gets the network name for an instance.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <returns>The network name.</returns>
    public static string NetworkName(string instanceId) => ResourcePrefix + Require(instanceId);

    /// <summary>Gets the volume name for one node.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="nodeIndex">The one-based node index.</param>
    /// <returns>The volume name.</returns>
    public static string VolumeName(string instanceId, int nodeIndex) =>
        ResourcePrefix + Require(instanceId) + "-n" + nodeIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the container name for one node.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="roleName">The node's role name, for example <c>sentinel2</c>.</param>
    /// <returns>The container name.</returns>
    public static string ContainerName(string instanceId, string roleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        return ResourcePrefix + Require(instanceId) + "-" + roleName;
    }

    /// <summary>Tests a name against Docker's own resource-name rule and length limit.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>True when the daemon will accept the name.</returns>
    public static bool IsValidResourceName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 63)
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_' && character != '.'
                && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static string Require(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return instanceId;
    }
}
