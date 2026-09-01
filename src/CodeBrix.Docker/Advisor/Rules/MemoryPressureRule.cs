namespace CodeBrix.Docker;

/// <summary>
/// CB006 — the running container's application memory is within 10% of its limit, so an OOM kill is
/// close.
/// </summary>
internal sealed class MemoryPressureRule : IAdvisorRule
{
    private const double PressurePercent = 90d;

    public string RuleId => "CB006";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var limit = context.MemoryLimitBytes;
        var anon = context.Stats?.MemoryStats?.AnonBytes;
        if (limit is not > 0 || anon is null)
        {
            return null;
        }

        var percent = (double)anon.Value / limit.Value * 100d;
        if (percent < PressurePercent)
        {
            return null;
        }

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "Application memory is close to the limit",
            $"Container '{context.ContainerName}' holds {DiagnosticsFormatting.Bytes(anon.Value)} of anonymous " +
            $"(non-reclaimable) memory against its {DiagnosticsFormatting.Bytes(limit.Value)} limit " +
            $"({DiagnosticsFormatting.Percent(percent)}); page cache cannot be reclaimed to save it, so the " +
            "next allocation spike ends in an OOM kill.",
            "Raise ResourceLimits.MemoryBytes (docker run --memory) for this container, or reduce the " +
            "workload's in-memory footprint (cache sizes, heap settings, concurrency).");
    }
}
