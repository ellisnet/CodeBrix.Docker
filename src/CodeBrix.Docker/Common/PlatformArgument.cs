using System;

namespace CodeBrix.Docker;

/// <summary>
/// Normalizes the one-platform values accepted by <see cref="ImageBuildSpec.Platform"/>,
/// <see cref="ContainerSpec.Platform"/> and <see cref="ImagePullOptions.Platform"/>.
/// </summary>
internal static class PlatformArgument
{
    /// <summary>
    /// Returns the trimmed platform, or <see langword="null"/> when <paramref name="platform"/> is
    /// <see langword="null"/> or blank.
    /// </summary>
    /// <param name="platform">The caller's value, for example <c>linux/arm64</c>.</param>
    /// <param name="member">The member the value came from, for the exception message.</param>
    /// <param name="paramName">The parameter to name in the exception.</param>
    /// <exception cref="ArgumentException">The value names more than one platform.</exception>
    public static string Normalize(string platform, string member, string paramName)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var trimmed = platform.Trim();
        if (trimmed.Contains(',', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{member} takes exactly one platform (for example \"linux/arm64\"), not the list '{trimmed}'. " +
                "A multi-platform image needs a registry push, which this library does not do.", paramName);
        }

        return trimmed;
    }
}
