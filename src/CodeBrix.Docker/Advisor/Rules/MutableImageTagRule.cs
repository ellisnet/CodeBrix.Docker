using System;

namespace CodeBrix.Docker;

/// <summary>
/// CB014 — the container was created from a moving reference (<c>:latest</c> or no tag at all), so
/// the same spec can produce a different image tomorrow.
/// </summary>
internal sealed class MutableImageTagRule : IAdvisorRule
{
    public string RuleId => "CB014";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var reference = context.Inspect.Config?.Image;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var tag = ExtractTag(reference);
        if (tag is not (null or "latest"))
        {
            return null;
        }

        var observed = tag is null
            ? $"Config.Image is '{reference}', which carries no tag and therefore resolves to ':latest'"
            : $"Config.Image is '{reference}'";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Info,
            context.ContainerName,
            "Image reference is not pinned",
            $"{observed} for container '{context.ContainerName}', so a later pull of the same spec can start a " +
            "different image — deploys stop being reproducible and rollbacks stop being reliable.",
            "Set ContainerSpec.Image to a pinned reference — an explicit version tag such as " +
            "\"nginx:1.27-alpine\", or a digest such as \"nginx@sha256:...\" for an exact match.");
    }

    /// <summary>
    /// Extracts the tag from an image reference, returning <see langword="null"/> when it carries none
    /// and <c>"(digest)"</c> for digest-pinned or id references, which are already immutable.
    /// </summary>
    private static string ExtractTag(string reference)
    {
        if (reference.Contains("@sha256:", StringComparison.Ordinal) || IsImageId(reference))
        {
            return "(digest)";
        }

        var lastSlash = reference.LastIndexOf('/');
        var lastColon = reference.LastIndexOf(':');

        // A colon before the last slash belongs to a registry port, not a tag.
        return lastColon > lastSlash && lastColon >= 0 ? reference[(lastColon + 1)..] : null;
    }

    private static bool IsImageId(string reference)
    {
        if (reference.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return true;
        }

        if (reference.Length is < 12 or > 64)
        {
            return false;
        }

        foreach (var c in reference)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
