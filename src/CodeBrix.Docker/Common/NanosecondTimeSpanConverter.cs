using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Converts between a <see cref="TimeSpan"/> and the integer nanosecond durations used by the
/// Docker Engine API (for example <c>Healthcheck.Interval</c>).
/// </summary>
internal sealed class NanosecondTimeSpanConverter : JsonConverter<TimeSpan?>
{
    private const long NanosecondsPerTick = 100;

    public override bool HandleNull => true;

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var nanoseconds))
        {
            return nanoseconds <= 0 ? null : TimeSpan.FromTicks(nanoseconds / NanosecondsPerTick);
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value.Ticks * NanosecondsPerTick);
    }
}
