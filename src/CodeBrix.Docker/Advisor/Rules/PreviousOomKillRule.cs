using System.Globalization;

namespace CodeBrix.Docker;

/// <summary>
/// CB012 — the container's last run ended in an OOM kill, which is the strongest possible evidence
/// that its memory limit is wrong for the workload.
/// </summary>
internal sealed class PreviousOomKillRule : IAdvisorRule
{
    public string RuleId => "CB012";

    public AdvisorFinding Evaluate(AdvisorContext context)
    {
        var state = context.Inspect.State;
        if (state is null)
        {
            return null;
        }

        var killedBySignal = !state.Running && state.ExitCode == 137;
        if (!state.OomKilled && !killedBySignal)
        {
            return null;
        }

        var limit = context.MemoryLimitBytes;
        var limitClause = limit is null
            ? "no memory limit is set on the container"
            : $"its memory limit is {DiagnosticsFormatting.Bytes(limit.Value)}";
        var whenClause = state.FinishedAt is null
            ? string.Empty
            : $" at {DiagnosticsFormatting.Timestamp(state.FinishedAt.Value)}";
        var restartClause = context.Inspect.RestartCount > 0
            ? $", after {DiagnosticsFormatting.Count(context.Inspect.RestartCount)} restart(s)"
            : string.Empty;

        var detail = state.OomKilled
            ? $"State.OOMKilled is true for container '{context.ContainerName}', which exited with code " +
              $"{state.ExitCode.ToString(CultureInfo.InvariantCulture)}{whenClause}{restartClause}: the kernel " +
              $"OOM killer terminated it, and {limitClause}."
            : $"Container '{context.ContainerName}' exited with code 137 (SIGKILL){whenClause}{restartClause}, " +
              $"the signature of an out-of-memory kill, and {limitClause}.";

        return new AdvisorFinding(
            RuleId,
            AdvisorSeverity.Warning,
            context.ContainerName,
            "Container was OOM-killed",
            detail,
            "Raise ResourceLimits.MemoryBytes (docker run --memory) to match the workload's real peak — use " +
            "DiagnosticsOperations.GetMemoryBreakdownAsync to size it from anonymous memory — or fix the leak " +
            "that drives the growth.");
    }
}
