using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.RedisManagement;
using System;
using System.Collections.Generic;

namespace RedisSetupTool.Services;

/// <summary>
/// Maps a Docker-side <see cref="TopologyInstance"/> onto the Redis-side
/// <see cref="RedisConnectionDescriptor"/> the verification and health tiers take. This is the
/// only place the two libraries meet: <c>RedisSetupTool.RedisManagement</c> deliberately knows
/// nothing about topologies, so the per-topology expectations it verifies against are filled
/// in here.
/// </summary>
public static class ConnectionMapper
{
    /// <summary>The five module names Redis 8 loads through its own entrypoint (topology F3).</summary>
    public static IReadOnlyList<string> Redis8Modules { get; } =
        ["search", "bf", "vectorset", "timeseries", "ReJSON"];

    /// <summary>
    /// Builds the descriptor for an instance. The two shape enums have the same five members
    /// in the same order, on purpose, so the cast is safe.
    /// </summary>
    /// <param name="instance">The instance to describe.</param>
    /// <param name="parameters">
    /// The parameter values the instance was created with, when they are still known. Only
    /// topology A6's eviction policy needs them: a discovered instance carries no label for it,
    /// so passing null simply drops that one expectation rather than asserting a wrong value.
    /// </param>
    /// <returns>The descriptor, or null when the instance carries no connection information.</returns>
    public static RedisConnectionDescriptor Map(TopologyInstance instance,
        IReadOnlyDictionary<string, string> parameters = null)
    {
        if (instance?.Connection is null)
        {
            return null;
        }

        var connection = instance.Connection;
        var endpoints = new List<RedisHostPort>();
        foreach (var endpoint in connection.Endpoints)
        {
            endpoints.Add(new RedisHostPort
            {
                Host = endpoint.Host,
                Port = endpoint.Port,
                Role = endpoint.Role.ToString(),
                IsSentinel = endpoint.IsSentinel,
            });
        }

        return new RedisConnectionDescriptor
        {
            //The two enums have the same five members in the same order, on purpose.
            Shape = (RedisConnectionShape)(int)connection.Shape,
            Endpoints = endpoints,
            ServiceName = connection.ServiceName,
            Credentials = string.IsNullOrEmpty(connection.Password)
                ? null
                : new RedisCredentials
                {
                    Username = connection.Username,
                    Password = connection.Password,
                },
            ExpectedConfig = ExpectedConfig(instance.TopologyId, parameters),
            ExpectedModules = instance.TopologyId == TopologyId.F3 ? Redis8Modules : [],
            ExpectedUsers = ExpectedUsers(connection),
            ExpectedVersionPrefix = instance.TopologyId == TopologyId.E3 ? "6.2" : null,
        };
    }

    /// <summary>
    /// Builds a descriptor aimed at one single node of an instance, which is what a per-node
    /// health sample needs: the shape collapses to standalone and the endpoint list holds one
    /// entry.
    /// </summary>
    /// <param name="instance">The instance the node belongs to.</param>
    /// <param name="node">The node to aim at.</param>
    /// <returns>The single-node descriptor, or null when either argument is missing.</returns>
    public static RedisConnectionDescriptor MapNode(TopologyInstance instance, TopologyNode node)
    {
        if (instance?.Connection is null || node is null)
        {
            return null;
        }

        var host = "127.0.0.1";
        foreach (var endpoint in instance.Connection.Endpoints)
        {
            if (endpoint.Port == node.HostPort)
            {
                host = endpoint.Host;
                break;
            }
        }

        return new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints =
            [
                new RedisHostPort
                {
                    Host = host,
                    Port = node.HostPort,
                    Role = node.Role.ToString(),
                    IsSentinel = node.Role == NodeRole.Sentinel,
                },
            ],
            Credentials = string.IsNullOrEmpty(instance.Connection.Password)
                ? null
                : new RedisCredentials
                {
                    Username = instance.Connection.Username,
                    Password = instance.Connection.Password,
                },
        };
    }

    private static IReadOnlyDictionary<string, string> ExpectedConfig(TopologyId topologyId,
        IReadOnlyDictionary<string, string> parameters)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);

        if (topologyId == TopologyId.A5)
        {
            //A5 is the persistence preset: the append-only log is always on.
            expected["appendonly"] = "yes";
        }

        if ((topologyId == TopologyId.A6 || topologyId == TopologyId.G1)
            && parameters is not null
            && parameters.TryGetValue("policy", out var policy)
            && !string.IsNullOrWhiteSpace(policy))
        {
            expected["maxmemory-policy"] = policy;
        }

        return expected;
    }

    private static IReadOnlyList<string> ExpectedUsers(ConnectionInfo connection)
    {
        if (connection.AdditionalUsers.Count == 0)
        {
            return [];
        }

        var users = new List<string>();
        foreach (var user in connection.AdditionalUsers)
        {
            if (!string.IsNullOrWhiteSpace(user.Username))
            {
                users.Add(user.Username);
            }
        }
        return users;
    }
}
