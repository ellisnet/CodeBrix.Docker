using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Traffic counters for one of a container's network interfaces.
/// </summary>
public sealed class NetworkStats
{
    /// <summary>Gets bytes received.</summary>
    [JsonPropertyName("rx_bytes")]
    public long? RxBytes { get; init; }

    /// <summary>Gets packets received.</summary>
    [JsonPropertyName("rx_packets")]
    public long? RxPackets { get; init; }

    /// <summary>Gets receive errors.</summary>
    [JsonPropertyName("rx_errors")]
    public long? RxErrors { get; init; }

    /// <summary>Gets packets dropped on receive.</summary>
    [JsonPropertyName("rx_dropped")]
    public long? RxDropped { get; init; }

    /// <summary>Gets bytes transmitted.</summary>
    [JsonPropertyName("tx_bytes")]
    public long? TxBytes { get; init; }

    /// <summary>Gets packets transmitted.</summary>
    [JsonPropertyName("tx_packets")]
    public long? TxPackets { get; init; }

    /// <summary>Gets transmit errors.</summary>
    [JsonPropertyName("tx_errors")]
    public long? TxErrors { get; init; }

    /// <summary>Gets packets dropped on transmit.</summary>
    [JsonPropertyName("tx_dropped")]
    public long? TxDropped { get; init; }
}
