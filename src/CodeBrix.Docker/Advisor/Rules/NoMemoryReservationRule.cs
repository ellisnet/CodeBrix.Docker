using System;
using System.Globalization;

namespace CodeBrix.Docker;

/// <summary>
/// CB009 — a hard memory limit is set without a soft reservation, so the scheduler has nothing to go
/// on when it decides where the container fits.
/// </summary>
internal sealed class NoMemoryReservationRule : IAdvisorRule
{
    private const double SuggestedFraction = 0.75;

    public string RuleId => "CB009";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var hostConfig = context.HostConfig;
        if (hostConfig is null || hostConfig.Memory <= 0 || hostConfig.MemoryReservation > 0)
        {
            return null;
        }

        var suggested = (long)(hostConfig.Memory * SuggestedFraction);
        var suggestedMegabytes = Math.Max(1, suggested / (1024 * 1024));

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Info,
            context.ContainerName,
            "Memory limit set without a reservation",
            $"Container '{context.ContainerName}' has a hard limit of " +
            $"{DiagnosticsFormatting.Bytes(hostConfig.Memory)} but HostConfig.MemoryReservation is 0, so the " +
            "kernel has no soft target to reclaim towards and schedulers cannot see what the container actually " +
            "needs.",
            "Set ResourceLimits.MemoryReservationBytes (docker run --memory-reservation) to roughly 70–80% of " +
            $"the limit — about {DiagnosticsFormatting.Bytes(suggested)}, i.e. " +
            $"ResourceLimits.Megabytes({suggestedMegabytes.ToString(CultureInfo.InvariantCulture)}).");
    }
}
