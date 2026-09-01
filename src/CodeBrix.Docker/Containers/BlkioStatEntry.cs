using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// One block-I/O counter, reported per device and operation.
/// </summary>
public sealed class BlkioStatEntry
{
    /// <summary>Gets the device major number.</summary>
    [JsonPropertyName("major")]
    public long? Major { get; init; }

    /// <summary>Gets the device minor number.</summary>
    [JsonPropertyName("minor")]
    public long? Minor { get; init; }

    /// <summary>Gets the operation, for example <c>read</c>, <c>write</c> or <c>total</c>.</summary>
    [JsonPropertyName("op")]
    public string Op { get; init; }

    /// <summary>Gets the counter value.</summary>
    [JsonPropertyName("value")]
    public long? Value { get; init; }
}
