namespace CodeBrix.Docker;

/// <summary>
/// CB010 — the container runs privileged, which turns off essentially every isolation boundary
/// between it and the host.
/// </summary>
internal sealed class PrivilegedContainerRule : IAdvisorRule
{
    public string RuleId => "CB010";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        if (context.HostConfig?.Privileged != true)
        {
            return null;
        }

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Critical,
            context.ContainerName,
            "Container runs privileged",
            $"HostConfig.Privileged is true for container '{context.ContainerName}', which grants it all Linux " +
            "capabilities, access to every host device and an unconfined seccomp/AppArmor profile — escaping to " +
            "the host from inside it is trivial.",
            "Set ContainerSpec.Privileged to false (drop docker run --privileged) and grant only the specific " +
            "capabilities or device mounts the workload actually needs.");
    }
}
