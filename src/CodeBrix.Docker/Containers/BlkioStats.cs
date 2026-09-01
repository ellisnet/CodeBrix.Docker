using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Block-I/O counters for a container.
/// </summary>
public sealed class BlkioStats
{
    /// <summary>Gets bytes transferred, per device and operation.</summary>
    [JsonPropertyName("io_service_bytes_recursive")]
    public IReadOnlyList<BlkioStatEntry> IoServiceBytesRecursive { get; init; }

    /// <summary>Gets operation counts, per device and operation.</summary>
    [JsonPropertyName("io_serviced_recursive")]
    public IReadOnlyList<BlkioStatEntry> IoServicedRecursive { get; init; }

    /// <summary>Gets queued operations, per device and operation.</summary>
    [JsonPropertyName("io_queue_recursive")]
    public IReadOnlyList<BlkioStatEntry> IoQueuedRecursive { get; init; }

    /// <summary>Gets service times, per device and operation.</summary>
    [JsonPropertyName("io_service_time_recursive")]
    public IReadOnlyList<BlkioStatEntry> IoServiceTimeRecursive { get; init; }

    /// <summary>Gets wait times, per device and operation.</summary>
    [JsonPropertyName("io_wait_time_recursive")]
    public IReadOnlyList<BlkioStatEntry> IoWaitTimeRecursive { get; init; }

    /// <summary>
    /// Sums <see cref="IoServiceBytesRecursive"/> for one operation.
    /// </summary>
    /// <param name="op">The operation, for example <c>read</c> or <c>write</c>. Matching is case-insensitive.</param>
    /// <returns>The total bytes, or <see langword="null"/> when the daemon reported no counters.</returns>
    public long? TotalBytes(string op)
    {
        if (IoServiceBytesRecursive is null)
        {
            return null;
        }

        long total = 0;
        foreach (var entry in IoServiceBytesRecursive)
        {
            if (string.Equals(entry.Op, op, StringComparison.OrdinalIgnoreCase))
            {
                total += entry.Value ?? 0;
            }
        }

        return total;
    }
}
