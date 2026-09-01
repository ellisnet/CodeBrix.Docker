using System;

namespace CodeBrix.Docker;

/// <summary>
/// CB008 — the container's main process runs as root, so a compromise inside the container starts
/// with uid 0.
/// </summary>
internal sealed class RunningAsRootRule : IAdvisorRule
{
    public string RuleId => "CB008";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var user = context.Inspect.Config?.User;
        if (!IsRoot(user))
        {
            return null;
        }

        var observed = string.IsNullOrWhiteSpace(user)
            ? "Config.User is empty, which means root"
            : $"Config.User is '{user}'";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "Container runs as root",
            $"{observed} for container '{context.ContainerName}', so anything that breaks the application " +
            "inside the container already has uid 0 there, and every bind-mounted host path is writable as root.",
            "Set ContainerSpec.User (docker run --user), for example \"1000:1000\" or a named account such as " +
            "\"nobody\", and add a matching USER instruction to the image.");
    }

    private static bool IsRoot(string user) =>
        string.IsNullOrWhiteSpace(user)
        || string.Equals(user, "root", StringComparison.OrdinalIgnoreCase)
        || user == "0"
        || user == "0:0"
        || string.Equals(user, "root:root", StringComparison.OrdinalIgnoreCase);
}
