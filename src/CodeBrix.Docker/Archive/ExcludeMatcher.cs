using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.Docker;

/// <summary>
/// Matches relative, forward-slash paths against the glob patterns of
/// <see cref="CopyToContainerOptions.ExcludePatterns"/>.
/// </summary>
internal sealed class ExcludeMatcher
{
    private readonly List<Rule> _rules = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ExcludeMatcher"/> class.
    /// </summary>
    /// <param name="patterns">The patterns; <see langword="null"/> and blank entries are ignored.</param>
    public ExcludeMatcher(IEnumerable<string> patterns)
    {
        if (patterns is null)
        {
            return;
        }

        foreach (var raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var pattern = raw.Trim().Replace('\\', '/');
            while (pattern.StartsWith("./", StringComparison.Ordinal))
            {
                pattern = pattern[2..];
            }

            var directoryOnly = pattern.EndsWith('/');
            pattern = pattern.TrimEnd('/');

            var anchored = pattern.Contains('/');
            pattern = pattern.TrimStart('/');

            if (pattern.Length == 0)
            {
                continue;
            }

            _rules.Add(new Rule(new Regex("^" + ToRegex(pattern) + "$", RegexOptions.CultureInvariant),
                anchored, directoryOnly));
        }
    }

    /// <summary>Gets a value indicating whether no patterns were supplied.</summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>
    /// Returns whether a path is excluded.
    /// </summary>
    /// <param name="relativePath">The path relative to the copied folder, with forward slashes.</param>
    /// <param name="isDirectory">Whether the path is a directory.</param>
    /// <returns><see langword="true"/> when any pattern matches.</returns>
    public bool IsExcluded(string relativePath, bool isDirectory)
    {
        if (_rules.Count == 0)
        {
            return false;
        }

        var slash = relativePath.LastIndexOf('/');
        var name = slash < 0 ? relativePath : relativePath[(slash + 1)..];

        foreach (var rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (rule.Pattern.IsMatch(rule.Anchored ? relativePath : name))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToRegex(string glob)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                var doubleStar = i + 1 < glob.Length && glob[i + 1] == '*';
                if (!doubleStar)
                {
                    builder.Append("[^/]*");
                    continue;
                }

                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    // "**/" matches zero or more whole segments.
                    i++;
                    builder.Append("(?:.*/)?");
                }
                else
                {
                    builder.Append(".*");
                }
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.ToString();
    }

    private sealed record Rule(Regex Pattern, bool Anchored, bool DirectoryOnly);
}
