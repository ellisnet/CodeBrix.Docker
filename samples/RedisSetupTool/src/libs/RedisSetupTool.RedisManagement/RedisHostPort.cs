using System.Globalization;

namespace RedisSetupTool.RedisManagement;

/// <summary>One address a client can dial.</summary>
public sealed class RedisHostPort
{
    /// <summary>Gets the host.</summary>
    public string Host { get; init; }

    /// <summary>Gets the port.</summary>
    public int Port { get; init; }

    /// <summary>Gets free text describing the node, for display.</summary>
    public string Role { get; init; }

    /// <summary>Gets a value indicating whether the address is a sentinel.</summary>
    public bool IsSentinel { get; init; }

    /// <inheritdoc />
    public override string ToString() => Host + ":" + Port.ToString(CultureInfo.InvariantCulture);
}
