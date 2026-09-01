namespace CodeBrix.Docker;

/// <summary>
/// CB002 — a memory limit is set but swap is not pinned to it, so the container can swap instead of
/// failing fast, and its performance becomes unpredictable.
/// </summary>
internal sealed class SwapNotDisabledRule : IAdvisorRule
{
    public string RuleId => "CB002";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var hostConfig = context.HostConfig;
        if (hostConfig is null || hostConfig.Memory <= 0 || hostConfig.MemorySwap == hostConfig.Memory)
        {
            return null;
        }

        var swapDescription = hostConfig.MemorySwap switch
        {
            -1 => "unlimited swap",
            0 => "the daemon default of twice the memory limit",
            var swap => $"{DiagnosticsFormatting.Bytes(swap)} of combined memory + swap",
        };

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "Memory limit set but swap is not disabled",
            $"Container '{context.ContainerName}' is limited to " +
            $"{DiagnosticsFormatting.Bytes(hostConfig.Memory)} of memory but HostConfig.MemorySwap allows " +
            $"{swapDescription}; the workload will silently swap instead of failing fast, and latency becomes " +
            "unpredictable.",
            "Set ResourceLimits.MemorySwapBytes equal to ResourceLimits.MemoryBytes " +
            "(--memory-swap == --memory) to disable swap for this container.");
    }
}
