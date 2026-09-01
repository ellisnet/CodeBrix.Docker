namespace CodeBrix.Docker;

/// <summary>
/// CB003 — the container has no process cap, so a fork bomb or a runaway thread pool can exhaust the
/// host's PID space.
/// </summary>
internal sealed class NoPidsLimitRule : IAdvisorRule
{
    public string RuleId => "CB003";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var hostConfig = context.HostConfig;
        if (hostConfig is null || hostConfig.PidsLimit is > 0)
        {
            return null;
        }

        var current = context.Stats?.PidsStats?.Current;
        var observed = current is null
            ? string.Empty
            : $" It is currently running {DiagnosticsFormatting.Count(current.Value)} process(es)/thread(s).";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "No PID limit set",
            $"HostConfig.PidsLimit is not set for container '{context.ContainerName}', so a fork bomb or a " +
            $"runaway thread pool inside it can exhaust the host's PID space and take down unrelated " +
            $"workloads.{observed}",
            "Set ResourceLimits.PidsLimit on the container spec (docker run --pids-limit), for example 256, " +
            "comfortably above the container's normal process/thread count.");
    }
}
