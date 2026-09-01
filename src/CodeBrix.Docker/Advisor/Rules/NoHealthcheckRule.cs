namespace CodeBrix.Docker;

/// <summary>
/// CB007 — neither the image nor the container defines a healthcheck, so nothing distinguishes a
/// running process from a working service.
/// </summary>
internal sealed class NoHealthcheckRule : IAdvisorRule
{
    public string RuleId => "CB007";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        if (DiagnosticsOperations.HasHealthcheck(context.Inspect))
        {
            return null;
        }

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "No healthcheck defined",
            $"Container '{context.ContainerName}' has no HEALTHCHECK from its image and none in its own " +
            "configuration, so the daemon reports it as up the moment its process starts — a deadlocked or " +
            "unresponsive service looks identical to a working one, and orchestrators will keep routing to it.",
            "Set ContainerSpec.Healthcheck (docker run --health-cmd), for example " +
            "[\"CMD-SHELL\", \"wget -q -O /dev/null http://localhost/ || exit 1\"] with an interval and retries, " +
            "or add a HEALTHCHECK instruction to the image.");
    }
}
