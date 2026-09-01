namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One layer in an efficiency report.</summary>
public sealed class EfficiencyLayerInfo
{
    /// <summary>Gets the zero-based layer index.</summary>
    public int Index { get; init; }

    /// <summary>Gets the layer size, in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the build instruction that produced the layer.</summary>
    public string Command { get; init; }

    /// <summary>Gets the layer digest.</summary>
    public string Digest { get; init; }
}
