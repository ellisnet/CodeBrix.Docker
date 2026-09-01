using System.Globalization;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One host-reachable address of an instance.</summary>
public sealed class RedisEndpoint
{
    /// <summary>Gets the host address.</summary>
    public string Host { get; init; }

    /// <summary>Gets the host port.</summary>
    public int Port { get; init; }

    /// <summary>Gets what the node behind the endpoint does.</summary>
    public NodeRole Role { get; init; }

    /// <summary>Gets the one-based node index.</summary>
    public int NodeIndex { get; init; }

    /// <summary>Gets a value indicating whether the endpoint is a sentinel.</summary>
    public bool IsSentinel { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        Host + ":" + Port.ToString(CultureInfo.InvariantCulture);
}
