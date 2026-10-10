using System;
using System.Globalization;

namespace CodeBrix.Docker;

/// <summary>
/// Parses the human-readable sizes the docker CLI prints, such as <c>"0B"</c>, <c>"1.5kB"</c>,
/// <c>"14.56GB"</c> or <c>"2GiB"</c>.
/// </summary>
internal static class DockerSizeText
{
    private const string TotalPrefix = "Total:";

    /// <summary>
    /// Converts a CLI size to bytes. Decimal units (<c>kB</c>, <c>MB</c>, <c>GB</c>, <c>TB</c>, <c>PB</c>)
    /// are 1000-based, binary units (<c>KiB</c>, <c>MiB</c>, <c>GiB</c>, <c>TiB</c>, <c>PiB</c>) are
    /// 1024-based, and <c>B</c> is bytes. Unit letters are matched ignoring case.
    /// </summary>
    /// <param name="text">The size text.</param>
    /// <returns>The size in bytes, rounded to the nearest byte, or <see langword="null"/> when it cannot be parsed.</returns>
    internal static long? ParseBytes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var split = 0;
        while (split < trimmed.Length && (char.IsAsciiDigit(trimmed[split]) || trimmed[split] == '.'))
        {
            split++;
        }

        if (split == 0
            || !decimal.TryParse(trimmed.AsSpan(0, split), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        long? multiplier = trimmed[split..].Trim().ToUpperInvariant() switch
        {
            "B" => 1L,
            "KB" => 1000L,
            "MB" => 1000L * 1000,
            "GB" => 1000L * 1000 * 1000,
            "TB" => 1000L * 1000 * 1000 * 1000,
            "PB" => 1000L * 1000 * 1000 * 1000 * 1000,
            "KIB" => 1024L,
            "MIB" => 1024L * 1024,
            "GIB" => 1024L * 1024 * 1024,
            "TIB" => 1024L * 1024 * 1024 * 1024,
            "PIB" => 1024L * 1024 * 1024 * 1024 * 1024,
            _ => null,
        };

        if (multiplier is null)
        {
            return null;
        }

        try
        {
            return (long)Math.Round(number * multiplier.Value, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the value of the last <c>Total:</c> line in <c>docker builder prune</c> output.
    /// </summary>
    /// <param name="output">The CLI output.</param>
    /// <returns>The trimmed text after <c>Total:</c>, or <see langword="null"/> when there is no such line.</returns>
    internal static string FindTotal(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        string total = null;
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(TotalPrefix, StringComparison.OrdinalIgnoreCase))
            {
                total = trimmed[TotalPrefix.Length..].Trim();
            }
        }

        return total;
    }
}
