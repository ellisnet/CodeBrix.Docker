using System;
using System.Collections.Generic;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement.Mapping;

/// <summary>Turns CodeBrix.Docker diagnostics and advisor types into this library's DTOs.</summary>
internal static class DiagnosticsMapper
{
    internal static CpuThrottlingInfo ToInfo(CpuThrottlingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new CpuThrottlingInfo
        {
            HasLiveData = report.HasLiveData,
            Periods = report.Periods,
            ThrottledPeriods = report.ThrottledPeriods,
            ThrottledTime = report.ThrottledTime,
            ThrottleRatio = report.ThrottleRatio,
            Severity = (ThrottleLevel)(int)report.Severity,
            Interpretation = report.Interpretation,
        };
    }

    internal static MemoryBreakdownInfo ToInfo(MemoryBreakdownReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new MemoryBreakdownInfo
        {
            HasLiveData = report.HasLiveData,
            UsageBytes = report.UsageBytes,
            LimitBytes = report.LimitBytes,
            AnonBytes = report.AnonBytes,
            FileBytes = report.FileBytes,
            KernelBytes = report.KernelBytes,
            UsagePercent = report.UsagePercent,
            EffectiveUsagePercent = report.EffectiveUsagePercent,
            IsPageCacheDominated = report.IsPageCacheDominated,
            Interpretation = report.Interpretation,
        };
    }

    internal static OomInfo ToInfo(OomReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new OomInfo
        {
            IsRunning = report.IsRunning,
            WasOomKilled = report.WasOomKilled,
            ExitCode = report.ExitCode,
            RestartCount = report.RestartCount,
            FinishedAt = report.FinishedAt,
            MemoryLimitBytes = report.MemoryLimitBytes,
            Interpretation = report.Interpretation,
        };
    }

    internal static HealthInfo ToInfo(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var checks = new List<HealthCheckEntry>();
        foreach (var entry in report.RecentLogs ?? [])
        {
            checks.Add(new HealthCheckEntry
            {
                Start = entry.Start,
                End = entry.End,
                ExitCode = entry.ExitCode,
                Output = entry.Output ?? string.Empty,
            });
        }

        return new HealthInfo
        {
            HasHealthcheck = report.HasHealthcheck,
            Status = report.Status,
            FailingStreak = report.FailingStreak,
            IsHealthy = report.IsHealthy,
            Interpretation = report.Interpretation,
            RecentChecks = checks,
        };
    }

    internal static DiagnosticsReport ToReport(ContainerDiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new DiagnosticsReport
        {
            ContainerId = report.ContainerId,
            ContainerName = report.ContainerName,
            Status = report.Status,
            IsRunning = report.IsRunning,
            Summary = report.Summary,
            Cpu = ToInfo(report.CpuThrottling),
            Memory = ToInfo(report.Memory),
            Oom = ToInfo(report.Oom),
            Health = ToInfo(report.Health),
        };
    }

    internal static AdvisorFindingInfo ToInfo(AdvisorFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new AdvisorFindingInfo
        {
            RuleId = finding.RuleId,
            Severity = (AdvisorLevel)(int)finding.Severity,
            ContainerName = finding.ContainerName,
            Title = finding.Title,
            Detail = finding.Detail,
            Recommendation = finding.Recommendation,
        };
    }
}
