using System;

namespace RedisSetupTool.RedisManagement.Redlock;

/// <summary>What one master said when the lock was asked for.</summary>
public sealed class RedlockAcquisition
{
    /// <summary>Gets the master's address.</summary>
    public string Endpoint { get; init; }

    /// <summary>Gets a value indicating whether the master granted the lock.</summary>
    public bool Acquired { get; init; }

    /// <summary>Gets how long the master took to answer.</summary>
    public TimeSpan RoundTrip { get; init; }

    /// <summary>Gets what went wrong, when something did.</summary>
    public string ErrorMessage { get; init; }
}
