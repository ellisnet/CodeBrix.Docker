using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Reads a JSON object of numbers into a <see cref="long"/> dictionary, clamping values that fall
/// outside <see cref="long"/>'s range instead of failing.
/// </summary>
/// <remarks>
/// The kernel reports cgroup counters as unsigned 64-bit values and uses <c>UINT64_MAX</c> as
/// "unlimited"; a strict reader would throw on those entries and lose the whole stats payload.
/// </remarks>
internal sealed class TolerantLongDictionaryConverter : JsonConverter<IReadOnlyDictionary<string, long>>
{
    public override bool HandleNull => true;

    public override IReadOnlyDictionary<string, long> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return null;
        }

        var result = new Dictionary<string, long>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var name = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (name is null)
            {
                reader.Skip();
                continue;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt64(out var value))
                {
                    result[name] = value;
                }
                else if (reader.TryGetDouble(out var approximate))
                {
                    result[name] = approximate >= long.MaxValue
                        ? long.MaxValue
                        : approximate <= long.MinValue ? long.MinValue : (long)approximate;
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, long> value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var (key, entry) in value)
        {
            writer.WriteNumber(key, entry);
        }

        writer.WriteEndObject();
    }
}
