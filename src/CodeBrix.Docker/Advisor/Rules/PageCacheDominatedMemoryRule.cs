namespace CodeBrix.Docker;

/// <summary>
/// CB013 — most of the memory charged to the running container is reclaimable page cache, so its
/// usage figure reads far worse than the workload actually is.
/// </summary>
internal sealed class PageCacheDominatedMemoryRule : IAdvisorRule
{
    private const long MinimumFileBytes = 4L * 1024 * 1024;

    public string RuleId => "CB013";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var memory = context.Stats?.MemoryStats;
        var usage = memory?.Usage;
        var file = memory?.FileBytes;
        if (usage is not > 0 || file is null || file.Value < MinimumFileBytes)
        {
            return null;
        }

        var anon = memory?.AnonBytes ?? 0;
        if (file.Value <= 2 * anon || file.Value <= usage.Value / 2)
        {
            return null;
        }

        var cachePercent = (double)file.Value / usage.Value * 100d;

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Info,
            context.ContainerName,
            "Memory usage is dominated by page cache",
            $"Of the {DiagnosticsFormatting.Bytes(usage.Value)} charged to container " +
            $"'{context.ContainerName}', {DiagnosticsFormatting.Bytes(file.Value)} " +
            $"({DiagnosticsFormatting.Percent(cachePercent)}) is reclaimable page cache and only " +
            $"{DiagnosticsFormatting.Bytes(anon)} is application memory, so the usage number reported by " +
            "docker stats looks alarming while the workload is small.",
            "Size ResourceLimits.MemoryBytes from the anonymous figure " +
            "(DiagnosticsOperations.GetMemoryBreakdownAsync reports it as AnonBytes) rather than from total " +
            "usage, and do not raise the limit on the strength of the cache alone — the kernel reclaims it " +
            "under pressure.");
    }
}
