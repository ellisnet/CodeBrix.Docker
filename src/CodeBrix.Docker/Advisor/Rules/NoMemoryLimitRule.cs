namespace CodeBrix.Docker;

/// <summary>
/// CB001 — the container has no memory limit, so a leak or a burst can consume all host memory and
/// leave the kernel to pick a victim.
/// </summary>
internal sealed class NoMemoryLimitRule : IAdvisorRule
{
    public string RuleId => "CB001";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        if (context.HostConfig is null || context.HostConfig.Memory > 0)
        {
            return null;
        }

        var usage = context.Stats?.MemoryStats?.Usage;
        var observed = usage is null
            ? string.Empty
            : $" It is currently using {DiagnosticsFormatting.Bytes(usage.Value)}.";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "No memory limit set",
            $"HostConfig.Memory is 0, so container '{context.ContainerName}' may use all memory on the host; " +
            $"under pressure the kernel OOM killer chooses which process dies, and it need not be this one.{observed}",
            "Set ResourceLimits.MemoryBytes on the container spec (docker run --memory), for example " +
            "ResourceLimits.Megabytes(512), sized from the container's observed anonymous memory plus headroom.");
    }
}
