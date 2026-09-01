using System.Globalization;

namespace CodeBrix.Docker;

/// <summary>
/// CB005 — the running container is being throttled by its CPU quota in more than a quarter of
/// scheduling periods, so the limit is costing it real time.
/// </summary>
internal sealed class CpuThrottlingRule : IAdvisorRule
{
    private const double WarningRatio = 0.25;
    private const double CriticalRatio = 0.75;

    public string RuleId => "CB005";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var throttling = context.Stats?.CpuStats?.ThrottlingData;
        var periods = throttling?.Periods ?? 0;
        if (throttling is null || periods <= 0)
        {
            return null;
        }

        var ratio = (double)(throttling.ThrottledPeriods ?? 0) / periods;
        if (ratio <= WarningRatio)
        {
            return null;
        }

        var severity = ratio > CriticalRatio ? AdvisorSeverity.Critical : AdvisorSeverity.Warning;
        var cpus = context.HostConfig?.Cpus;
        var quota = cpus is null
            ? "no CPU quota is recorded on the container"
            : $"the quota is {cpus.Value.ToString("0.##", CultureInfo.InvariantCulture)} CPU";

        return new AdvisorFinding(
            RuleId,
            severity,
            context.ContainerName,
            "CPU limit is throttling the workload",
            $"Container '{context.ContainerName}' was throttled in {DiagnosticsFormatting.Ratio(ratio)} of " +
            $"{DiagnosticsFormatting.Count(periods)} CFS scheduling periods, stalling for " +
            $"{DiagnosticsFormatting.Nanoseconds(throttling.ThrottledTime ?? 0)} in total, and {quota}.",
            "Raise ResourceLimits.Cpus (docker run --cpus) for this container, or reduce its worker/thread " +
            "count so its concurrency matches the quota; ContainerOperations.UpdateResourcesAsync applies a new " +
            "limit without a restart.");
    }
}
