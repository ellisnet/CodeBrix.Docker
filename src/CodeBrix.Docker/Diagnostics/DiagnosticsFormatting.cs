using System;
using System.Globalization;

namespace CodeBrix.Docker;

/// <summary>
/// Number formatting shared by the diagnostic interpretations and the advisor findings, so that both
/// speak about bytes, percentages and durations in the same way.
/// </summary>
internal static class DiagnosticsFormatting
{
    private const long Kibibyte = 1024L;
    private const long Mebibyte = 1024L * 1024L;
    private const long Gibibyte = 1024L * 1024L * 1024L;

    /// <summary>Formats a byte count as a short human-readable string, for example <c>50.0 MB</c>.</summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>The formatted value.</returns>
    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "unlimited";
        }

        if (bytes < Kibibyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        if (bytes < Mebibyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Kibibyte:0.#} KB");
        }

        if (bytes < Gibibyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Mebibyte:0.#} MB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Gibibyte:0.##} GB");
    }

    /// <summary>Formats an optional byte count, using <c>unknown</c> when the value is missing.</summary>
    /// <param name="bytes">The byte count, or <see langword="null"/>.</param>
    /// <returns>The formatted value.</returns>
    public static string Bytes(long? bytes) => bytes.HasValue ? Bytes(bytes.Value) : "unknown";

    /// <summary>Formats a percentage already expressed on a 0–100 scale.</summary>
    /// <param name="percent">The percentage.</param>
    /// <returns>The formatted value, for example <c>94%</c>.</returns>
    public static string Percent(double percent) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(percent, MidpointRounding.AwayFromZero):0}%");

    /// <summary>Formats a 0–1 ratio as a percentage.</summary>
    /// <param name="ratio">The ratio.</param>
    /// <returns>The formatted value, for example <c>97%</c>.</returns>
    public static string Ratio(double ratio) => Percent(ratio * 100d);

    /// <summary>Formats a count with thousands separators.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The formatted value.</returns>
    public static string Count(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Formats a nanosecond duration in seconds.</summary>
    /// <param name="nanoseconds">The duration in nanoseconds.</param>
    /// <returns>The formatted value, for example <c>12.3s</c>.</returns>
    public static string Nanoseconds(long nanoseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{nanoseconds / 1_000_000_000d:0.##}s");

    /// <summary>Formats a timestamp in round-trip UTC form.</summary>
    /// <param name="timestamp">The timestamp.</param>
    /// <returns>The formatted value.</returns>
    public static string Timestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
