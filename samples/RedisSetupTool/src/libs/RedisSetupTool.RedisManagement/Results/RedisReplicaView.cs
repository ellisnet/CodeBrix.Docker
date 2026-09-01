namespace RedisSetupTool.RedisManagement.Results;

/// <summary>One replica as the primary sees it.</summary>
public sealed class RedisReplicaView
{
    /// <summary>Gets the replica's address.</summary>
    public string Endpoint { get; init; }

    /// <summary>Gets the replication state word.</summary>
    public string State { get; init; }

    /// <summary>Gets the replication offset the replica has reached.</summary>
    public long Offset { get; init; }

    /// <summary>Gets how far behind the replica is, in seconds.</summary>
    public long LagSeconds { get; init; }
}
