using System;

namespace RedisSetupTool.RedisManagement.Results;

/// <summary>The outcome of a reachability check.</summary>
public sealed class RedisPingResult
{
    /// <summary>Gets a value indicating whether the node answered.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets how long the round trip took.</summary>
    public TimeSpan RoundTrip { get; init; }

    /// <summary>Gets the endpoint that was dialled.</summary>
    public string Endpoint { get; init; }

    /// <summary>Gets what went wrong, when something did.</summary>
    public string ErrorMessage { get; init; }
}
