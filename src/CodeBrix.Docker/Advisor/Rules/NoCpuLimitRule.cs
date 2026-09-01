namespace CodeBrix.Docker;

/// <summary>
/// CB004 — neither a CPU quota nor a non-default CPU weight is set, so the container competes freely
/// with everything else on the host.
/// </summary>
internal sealed class NoCpuLimitRule : IAdvisorRule
{
    public string RuleId => "CB004";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var hostConfig = context.HostConfig;
        if (hostConfig is null || hostConfig.NanoCpus > 0)
        {
            return null;
        }

        if (hostConfig.CpuShares is not (0 or 1024))
        {
            return null;
        }

        var cpuPercent = context.Stats?.CpuPercent();
        var observed = cpuPercent is null
            ? string.Empty
            : $" It is currently drawing {DiagnosticsFormatting.Percent(cpuPercent.Value)} of one CPU.";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Info,
            context.ContainerName,
            "No CPU limit set",
            $"HostConfig.NanoCpus is 0 and CpuShares is at the default for container " +
            $"'{context.ContainerName}', so it can use every core on the host and starve its " +
            $"neighbours during a burst.{observed}",
            "Set ResourceLimits.Cpus on the container spec (docker run --cpus), for example 1.0, or set " +
            "ResourceLimits.CpuShares to express a relative priority instead of a hard cap.");
    }
}
