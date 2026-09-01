using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Process/thread counts for a container.
/// </summary>
public sealed class PidsStats
{
    /// <summary>Gets the number of processes and threads currently running.</summary>
    [JsonPropertyName("current")]
    public long? Current { get; init; }

    /// <summary>
    /// Gets the configured PID limit, or <see langword="null"/> when the container has none.
    /// </summary>
    /// <remarks>
    /// The daemon reports this as an unsigned 64-bit value and uses <c>ulong.MaxValue</c> — the cgroup
    /// v2 <c>pids.max</c> value <c>max</c> — to mean "no limit", which does not fit in an
    /// <see cref="long"/>; that case reads as <see langword="null"/> here.
    /// </remarks>
    [JsonPropertyName("limit")]
    [JsonConverter(typeof(UnlimitedAsNullInt64Converter))]
    public long? Limit { get; init; }

    /// <summary>
    /// Reads a possibly-unsigned cgroup counter, mapping anything outside the <see cref="long"/> range
    /// (the daemon's "unlimited" sentinel) to <see langword="null"/> instead of throwing.
    /// </summary>
    internal sealed class UnlimitedAsNullInt64Converter : JsonConverter<long?>
    {
        public override bool HandleNull => true;

        public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetInt64(out var value) ? value : null;

                case JsonTokenType.String:
                    return long.TryParse(reader.GetString(), out var parsed) ? parsed : null;

                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteNumberValue(value.Value);
            }
        }
    }
}
