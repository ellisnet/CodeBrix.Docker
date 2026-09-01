using System;

namespace RedisSetupTool.RedisManagement.Health;

/// <summary>One health reading of a deployment.</summary>
public sealed class RedisHealthSample
{
    /// <summary>Gets a value indicating whether the deployment answered.</summary>
    public bool Reachable { get; init; }

    /// <summary>Gets how long the round trip took.</summary>
    public TimeSpan RoundTrip { get; init; }

    /// <summary>Gets the role the node reported.</summary>
    public string Role { get; init; }

    /// <summary>Gets how much memory the node uses, in bytes.</summary>
    public long UsedMemoryBytes { get; init; }

    /// <summary>Gets how many clients are connected.</summary>
    public long ConnectedClients { get; init; }

    /// <summary>Gets when the sample was taken.</summary>
    public DateTimeOffset SampledAt { get; init; }

    /// <summary>Gets what went wrong, when something did.</summary>
    public string ErrorMessage { get; init; }
}
