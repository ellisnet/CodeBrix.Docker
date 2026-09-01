using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Tolerant converter for the timestamps the Docker daemon emits.
/// </summary>
/// <remarks>
/// The daemon uses Go's RFC 3339 "nano" format, which can carry nine fractional-second digits —
/// more than <see cref="JsonSerializer"/>'s built-in <see cref="DateTimeOffset"/> reader accepts.
/// It also uses the Go zero time (<c>0001-01-01T00:00:00Z</c>) to mean "never", which this
/// converter maps to <see langword="null"/>.
/// </remarks>
internal sealed class DockerDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.Number:
                // Some endpoints report Unix seconds.
                return reader.TryGetInt64(out var unixSeconds) && unixSeconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                    : null;

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
    }

    internal static DateTimeOffset? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!TryParseCore(text, out var value) && !TryParseCore(TrimFractionalSeconds(text), out value))
        {
            return null;
        }

        // Go's zero time means "not set".
        return value == DateTimeOffset.MinValue ? null : value;
    }

    private static bool TryParseCore(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out value);

    /// <summary>
    /// Truncates a fractional-seconds component to the seven digits <see cref="DateTimeOffset"/> supports.
    /// </summary>
    private static string TrimFractionalSeconds(string text)
    {
        var dot = text.IndexOf('.');
        if (dot < 0)
        {
            return text;
        }

        var end = dot + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var digits = end - dot - 1;
        if (digits <= 7)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, dot + 8), text.AsSpan(end));
    }
}
