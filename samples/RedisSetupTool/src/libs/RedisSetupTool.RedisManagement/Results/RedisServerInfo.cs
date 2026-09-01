namespace RedisSetupTool.RedisManagement.Results;

/// <summary>What a node says about itself.</summary>
public sealed class RedisServerInfo
{
    /// <summary>Gets the reported version.</summary>
    public string Version { get; init; }

    /// <summary>Gets the mode: standalone, sentinel or cluster.</summary>
    public string Mode { get; init; }

    /// <summary>Gets the role: master or slave.</summary>
    public string Role { get; init; }

    /// <summary>Gets the operating system the node runs on.</summary>
    public string Os { get; init; }

    /// <summary>Gets the architecture width.</summary>
    public int ArchBits { get; init; }

    /// <summary>Gets how long the node has been up, in seconds.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>Gets how many clients are connected.</summary>
    public long ConnectedClients { get; init; }

    /// <summary>Gets how much memory the node uses, in bytes.</summary>
    public long UsedMemoryBytes { get; init; }

    /// <summary>Gets the node's own memory cap, in bytes; zero means none.</summary>
    public long MaxMemoryBytes { get; init; }

    /// <summary>Gets the eviction policy.</summary>
    public string MaxMemoryPolicy { get; init; }

    /// <summary>Gets a value indicating whether the append-only log is on.</summary>
    public bool AofEnabled { get; init; }

    /// <summary>Gets when the last snapshot was taken, as a Unix timestamp.</summary>
    public long RdbLastSaveTime { get; init; }

    /// <summary>Gets how many keys the node holds.</summary>
    public long TotalKeys { get; init; }
}
