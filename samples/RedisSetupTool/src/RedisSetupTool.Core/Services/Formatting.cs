using System;
using System.Globalization;

namespace RedisSetupTool.Services;

/// <summary>
/// The one place display strings are shaped: byte counts, durations, percentages and
/// relative times. Every view model formats through this class so the same number reads
/// the same way in every section.
/// </summary>
public static class Formatting
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Renders a byte count as a short human string, for example <c>1.4 GB</c>.</summary>
    /// <param name="bytes">The count to render.</param>
    /// <returns>The formatted size, or <c>0 B</c> for a non-positive count.</returns>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < ByteUnits.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        var digits = unit == 0 || value >= 100d ? 0 : value >= 10d ? 1 : 2;
        return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture) + " " + ByteUnits[unit];
    }

    /// <summary>Renders a nullable byte count, showing an em dash when the value is absent.</summary>
    /// <param name="bytes">The count to render, or null.</param>
    /// <returns>The formatted size, or an em dash.</returns>
    public static string Bytes(long? bytes) => bytes.HasValue ? Bytes(bytes.Value) : "—";

    /// <summary>Renders a percentage with one decimal place, for example <c>37.4%</c>.</summary>
    /// <param name="percent">The percentage, already scaled to 0-100.</param>
    /// <returns>The formatted percentage, or an em dash when the value is absent.</returns>
    public static string Percent(double? percent) => percent.HasValue
        ? percent.Value.ToString("F1", CultureInfo.InvariantCulture) + "%"
        : "—";

    /// <summary>Renders a plain number with thousands separators.</summary>
    /// <param name="value">The number to render.</param>
    /// <returns>The formatted number.</returns>
    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Renders a nullable number, showing an em dash when the value is absent.</summary>
    /// <param name="value">The number to render, or null.</param>
    /// <returns>The formatted number, or an em dash.</returns>
    public static string Number(long? value) => value.HasValue ? Number(value.Value) : "—";

    /// <summary>Renders a duration compactly, for example <c>2h 14m</c> or <c>820 ms</c>.</summary>
    /// <param name="duration">The duration to render.</param>
    /// <returns>The formatted duration.</returns>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalMilliseconds < 1000d)
        {
            return duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms";
        }
        if (duration.TotalSeconds < 90d)
        {
            return duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
        }
        if (duration.TotalMinutes < 90d)
        {
            return ((int)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m "
                + duration.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }
        if (duration.TotalHours < 48d)
        {
            return ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + "h "
                + duration.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
        }
        return ((int)duration.TotalDays).ToString(CultureInfo.InvariantCulture) + " days";
    }

    /// <summary>Renders how long ago a moment was, for example <c>12 minutes ago</c>.</summary>
    /// <param name="moment">The moment to describe, or null.</param>
    /// <returns>The relative description, or an em dash when the moment is absent.</returns>
    public static string Relative(DateTimeOffset? moment)
    {
        if (!moment.HasValue)
        {
            return "—";
        }

        var elapsed = DateTimeOffset.UtcNow - moment.Value.ToUniversalTime();
        if (elapsed < TimeSpan.Zero)
        {
            return "just now";
        }
        if (elapsed.TotalSeconds < 45d)
        {
            return "just now";
        }
        if (elapsed.TotalMinutes < 90d)
        {
            var minutes = Math.Max(1, (int)Math.Round(elapsed.TotalMinutes));
            return Plural(minutes, "minute") + " ago";
        }
        if (elapsed.TotalHours < 36d)
        {
            var hours = Math.Max(1, (int)Math.Round(elapsed.TotalHours));
            return Plural(hours, "hour") + " ago";
        }
        var days = Math.Max(1, (int)Math.Round(elapsed.TotalDays));
        return Plural(days, "day") + " ago";
    }

    /// <summary>Renders a moment as a local wall-clock time, for example <c>23:41:07</c>.</summary>
    /// <param name="moment">The moment to render, or null.</param>
    /// <returns>The formatted time, or an em dash when the moment is absent.</returns>
    public static string Clock(DateTimeOffset? moment) => moment.HasValue
        ? moment.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>Renders a count with its noun, pluralised, for example <c>3 instances</c>.</summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>The formatted phrase.</returns>
    public static string Plural(int count, string noun) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");

    /// <summary>Replaces a null or blank string with an em dash so no label renders empty.</summary>
    /// <param name="text">The text to show.</param>
    /// <returns>The text, or an em dash.</returns>
    public static string OrDash(string text) =>
        string.IsNullOrWhiteSpace(text) ? "—" : text;

    /// <summary>Joins a list of strings with a comma, returning an em dash when it is empty.</summary>
    /// <param name="values">The values to join.</param>
    /// <returns>The joined text, or an em dash.</returns>
    public static string Join(System.Collections.Generic.IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0)
        {
            return "—";
        }
        return string.Join(", ", values);
    }

    /// <summary>Trims a long single-line string to a maximum length, adding an ellipsis.</summary>
    /// <param name="text">The text to trim.</param>
    /// <param name="maximum">The greatest number of characters to keep.</param>
    /// <returns>The trimmed text.</returns>
    public static string Trim(string text, int maximum)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maximum)
        {
            return text ?? string.Empty;
        }
        return text[..Math.Max(1, maximum - 1)] + "…";
    }
}
