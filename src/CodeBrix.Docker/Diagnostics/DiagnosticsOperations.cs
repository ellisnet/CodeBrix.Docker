using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// CPU-throttling, memory-breakdown, OOM and health diagnostics for containers.
/// </summary>
/// <remarks>
/// Every method here turns raw cgroup counters into a report that says what the numbers mean and what
/// to change. Live counters exist only while a container runs: the CPU and memory reports for a
/// stopped container come back empty with an interpretation that says so, rather than throwing.
/// <see cref="CheckOomAsync"/> is the exception — it reads the inspect payload and is at its most
/// useful after the container has died.
/// </remarks>
public sealed class DiagnosticsOperations
{
    private const double ModerateThrottleRatio = 0.05;
    private const double HighThrottleRatio = 0.25;
    private const double CriticalThrottleRatio = 0.75;

    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ContainerOperations _containers;

    internal DiagnosticsOperations(DockerApiClient api) => _containers = new ContainerOperations(api);

    /// <summary>
    /// Reads the container's CFS throttling counters and grades how badly its CPU quota is biting.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The throttling report. For a container that is not running the counters are zero and
    /// <see cref="CpuThrottlingReport.HasLiveData"/> is <see langword="false"/>.
    /// </returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<CpuThrottlingReport> GetCpuThrottlingAsync(string idOrName,
        CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        var stats = await TryGetLiveStatsAsync(inspect, cancellationToken).ConfigureAwait(false);
        return BuildCpuThrottlingReport(inspect, stats);
    }

    /// <summary>
    /// Splits the container's memory usage into application memory and reclaimable page cache, so the
    /// headline usage number can be read correctly.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The memory report. For a container that is not running the byte counts are zero or
    /// <see langword="null"/> and <see cref="MemoryBreakdownReport.HasLiveData"/> is <see langword="false"/>.
    /// </returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<MemoryBreakdownReport> GetMemoryBreakdownAsync(string idOrName,
        CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        var stats = await TryGetLiveStatsAsync(inspect, cancellationToken).ConfigureAwait(false);
        return BuildMemoryBreakdownReport(inspect, stats);
    }

    /// <summary>
    /// Determines whether the kernel's out-of-memory killer terminated the container. This works on
    /// stopped containers, which is where it is normally needed.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The OOM report.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<OomReport> CheckOomAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        return BuildOomReport(inspect);
    }

    /// <summary>
    /// Reads the container's healthcheck state and its most recent probe results.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The health report. A container with no healthcheck yields
    /// <see cref="HealthReport.HasHealthcheck"/> <see langword="false"/> rather than an error.
    /// </returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<HealthReport> GetHealthAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        return BuildHealthReport(inspect);
    }

    /// <summary>
    /// Polls the container's health state until it reports healthy.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes once the container is healthy.</returns>
    /// <exception cref="DockerException">
    /// The container has no healthcheck, so it can never report healthy; or it exited while being
    /// waited on.
    /// </exception>
    /// <exception cref="TimeoutException">The container was still not healthy when the timeout expired.</exception>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task WaitForHealthyAsync(string idOrName, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        string lastStatus = null;
        long failingStreak = 0;

        while (true)
        {
            var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);

            if (!HasHealthcheck(inspect))
            {
                throw new DockerException(
                    $"Container '{inspect.DisplayName}' defines no healthcheck, so it can never report healthy. " +
                    "Add one via ContainerSpec.Healthcheck (or a HEALTHCHECK instruction in the image) before " +
                    "calling WaitForHealthyAsync.");
            }

            var health = inspect.State?.Health;
            lastStatus = health?.Status ?? lastStatus;
            failingStreak = health?.FailingStreak ?? failingStreak;

            if (health?.IsHealthy == true)
            {
                return;
            }

            var state = inspect.State;
            if (state is not null && !state.Running && !state.Restarting)
            {
                throw new DockerException(
                    $"Container '{inspect.DisplayName}' is '{state.Status ?? "not running"}' (exit code " +
                    $"{state.ExitCode.ToString(CultureInfo.InvariantCulture)}), so it will never become healthy.");
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    $"Container '{inspect.DisplayName}' did not become healthy within " +
                    $"{timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s; last health status was " +
                    $"'{lastStatus ?? "unknown"}' with a failing streak of " +
                    $"{failingStreak.ToString(CultureInfo.InvariantCulture)}.");
            }

            var delay = remaining < HealthPollInterval ? remaining : HealthPollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs every diagnostic against one container in a single pass, sharing one inspect payload and
    /// one statistics sample.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The aggregated report.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<ContainerDiagnosticsReport> DiagnoseAsync(string idOrName,
        CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        var stats = await TryGetLiveStatsAsync(inspect, cancellationToken).ConfigureAwait(false);

        var cpu = BuildCpuThrottlingReport(inspect, stats);
        var memory = BuildMemoryBreakdownReport(inspect, stats);
        var oom = BuildOomReport(inspect);
        var health = BuildHealthReport(inspect);

        return new ContainerDiagnosticsReport
        {
            ContainerId = inspect.Id,
            ContainerName = inspect.DisplayName,
            Status = inspect.State?.Status,
            IsRunning = inspect.IsRunning,
            CpuThrottling = cpu,
            Memory = memory,
            Oom = oom,
            Health = health,
            Summary = Summarize(inspect, cpu, memory, oom, health),
        };
    }

    /// <summary>
    /// Determines whether a container has an effective healthcheck, from either the image's
    /// <c>HEALTHCHECK</c> instruction or the container's own configuration.
    /// </summary>
    /// <param name="inspect">The inspect payload.</param>
    /// <returns><see langword="true"/> when a healthcheck will run.</returns>
    internal static bool HasHealthcheck(ContainerInspectResult inspect)
    {
        if (inspect.State?.Health is not null)
        {
            return true;
        }

        var test = inspect.Config?.Healthcheck?.Test;
        if (test is null || test.Count == 0)
        {
            return false;
        }

        // ["NONE"] is the daemon's way of disabling a healthcheck inherited from the image.
        return !string.Equals(test[0], "NONE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the container's configured hard memory limit, or <see langword="null"/> when unlimited.
    /// </summary>
    /// <param name="inspect">The inspect payload.</param>
    /// <returns>The limit in bytes.</returns>
    internal static long? ConfiguredMemoryLimit(ContainerInspectResult inspect) =>
        inspect.HostConfig is { Memory: > 0 } hostConfig ? hostConfig.Memory : null;

    /// <summary>
    /// Grades a throttle ratio.
    /// </summary>
    /// <param name="ratio">The fraction of periods throttled, between 0 and 1.</param>
    /// <returns>The severity band.</returns>
    internal static ThrottleSeverity Grade(double ratio) => ratio switch
    {
        > CriticalThrottleRatio => ThrottleSeverity.Critical,
        >= HighThrottleRatio => ThrottleSeverity.High,
        >= ModerateThrottleRatio => ThrottleSeverity.Moderate,
        _ => ThrottleSeverity.None,
    };

    private async Task<ContainerStats> TryGetLiveStatsAsync(ContainerInspectResult inspect,
        CancellationToken cancellationToken)
    {
        if (!inspect.IsRunning)
        {
            return null;
        }

        try
        {
            var stats = await _containers.GetStatsAsync(inspect.Id, cancellationToken).ConfigureAwait(false);
            return stats.HasLiveData ? stats : null;
        }
        catch (DockerContainerNotFoundException)
        {
            // The container stopped and was removed between the inspect and the sample.
            return null;
        }
        catch (DockerApiException)
        {
            // The container stopped between the inspect and the sample; treat it as having no live data.
            return null;
        }
    }

    private static CpuThrottlingReport BuildCpuThrottlingReport(ContainerInspectResult inspect, ContainerStats stats)
    {
        var name = inspect.DisplayName;
        var throttling = stats?.CpuStats?.ThrottlingData;

        if (stats is null || throttling is null)
        {
            return new CpuThrottlingReport
            {
                ContainerName = name,
                HasLiveData = false,
                Severity = ThrottleSeverity.None,
                Interpretation = NoLiveStatsSentence(inspect, "CPU throttling counters"),
            };
        }

        var periods = throttling.Periods ?? 0;
        var throttledPeriods = throttling.ThrottledPeriods ?? 0;
        var throttledNanos = throttling.ThrottledTime ?? 0;
        var ratio = periods > 0 ? (double)throttledPeriods / periods : 0d;
        var severity = Grade(ratio);
        var cpus = inspect.HostConfig?.Cpus;

        string interpretation;
        if (periods == 0)
        {
            interpretation = cpus is null
                ? $"Container '{name}' has recorded no CFS scheduling periods because no CPU quota is in " +
                  "effect (HostConfig.NanoCpus is 0), so CPU throttling cannot occur."
                : $"Container '{name}' has recorded no CFS scheduling periods yet despite a " +
                  $"{FormatCpus(cpus.Value)}-CPU quota; let it run under load and sample again.";
        }
        else
        {
            var quota = cpus is null
                ? "no CPU quota is set"
                : $"the quota is {FormatCpus(cpus.Value)} CPU";
            var head = $"Container '{name}' was throttled in {DiagnosticsFormatting.Ratio(ratio)} of " +
                       $"{DiagnosticsFormatting.Count(periods)} CPU scheduling periods, stalling for " +
                       $"{DiagnosticsFormatting.Nanoseconds(throttledNanos)} in total";

            interpretation = severity switch
            {
                ThrottleSeverity.Critical =>
                    $"{head}; the CPU limit is far too restrictive for this workload ({quota}) — raise " +
                    "ResourceLimits.Cpus or reduce the worker/thread count.",
                ThrottleSeverity.High =>
                    $"{head}; the CPU limit is holding this workload back ({quota}) — raise " +
                    "ResourceLimits.Cpus or reduce the worker/thread count.",
                ThrottleSeverity.Moderate =>
                    $"{head}; throttling is mild but will show up as latency spikes ({quota}) — consider a " +
                    "small increase to ResourceLimits.Cpus if response time matters.",
                _ =>
                    $"{head}; that is within normal range, so the CPU allowance is adequate ({quota}).",
            };
        }

        return new CpuThrottlingReport
        {
            ContainerName = name,
            HasLiveData = true,
            Periods = periods,
            ThrottledPeriods = throttledPeriods,
            ThrottledTimeNanos = throttledNanos,
            ThrottleRatio = ratio,
            Severity = severity,
            Interpretation = interpretation,
        };
    }

    private static MemoryBreakdownReport BuildMemoryBreakdownReport(ContainerInspectResult inspect,
        ContainerStats stats)
    {
        var name = inspect.DisplayName;
        var limit = ConfiguredMemoryLimit(inspect);
        var memory = stats?.MemoryStats;

        if (stats is null || memory?.Usage is null)
        {
            return new MemoryBreakdownReport
            {
                ContainerName = name,
                HasLiveData = false,
                LimitBytes = limit,
                Interpretation = NoLiveStatsSentence(inspect, "memory statistics"),
            };
        }

        var usage = memory.Usage.Value;
        var anon = memory.AnonBytes;
        var file = memory.FileBytes;
        var kernel = memory.KernelBytes;

        double? usagePercent = limit is > 0 ? (double)usage / limit.Value * 100d : null;
        double? effectivePercent = limit is > 0 && anon is not null ? (double)anon.Value / limit.Value * 100d : null;

        var pageCacheDominated = IsPageCacheDominated(usage, anon, file);

        var breakdown = anon is null && file is null
            ? string.Empty
            : $", of which {DiagnosticsFormatting.Bytes(anon)} is application memory and " +
              $"{DiagnosticsFormatting.Bytes(file)} is reclaimable page cache";

        string interpretation;
        if (limit is null)
        {
            interpretation =
                $"Container '{name}' is using {DiagnosticsFormatting.Bytes(usage)} with no memory limit set" +
                $"{breakdown}; set ResourceLimits.MemoryBytes so the container cannot starve the host.";
        }
        else if (effectivePercent >= 90d)
        {
            interpretation =
                $"Container '{name}' holds {DiagnosticsFormatting.Bytes(anon)} of application memory against a " +
                $"{DiagnosticsFormatting.Bytes(limit.Value)} limit " +
                $"({DiagnosticsFormatting.Percent(effectivePercent.Value)}), so an OOM kill is imminent — raise " +
                "ResourceLimits.MemoryBytes or reduce the workload's footprint.";
        }
        else if (pageCacheDominated)
        {
            var cachePercent = usage > 0 ? (double)(file ?? 0) / usage * 100d : 0d;
            interpretation =
                $"Of the {DiagnosticsFormatting.Bytes(usage)} charged to container '{name}', " +
                $"{DiagnosticsFormatting.Bytes(file)} ({DiagnosticsFormatting.Percent(cachePercent)}) is " +
                $"reclaimable page cache and only {DiagnosticsFormatting.Bytes(anon)} is application memory, so " +
                "the headline usage figure overstates real demand — size ResourceLimits.MemoryBytes against the " +
                "application figure.";
        }
        else
        {
            interpretation =
                $"Container '{name}' is using {DiagnosticsFormatting.Bytes(usage)} of its " +
                $"{DiagnosticsFormatting.Bytes(limit.Value)} limit " +
                $"({DiagnosticsFormatting.Percent(usagePercent ?? 0d)}){breakdown}, which is comfortably within " +
                "the limit.";
        }

        return new MemoryBreakdownReport
        {
            ContainerName = name,
            HasLiveData = true,
            UsageBytes = usage,
            LimitBytes = limit,
            AnonBytes = anon,
            FileBytes = file,
            KernelBytes = kernel,
            UsagePercent = usagePercent,
            EffectiveUsagePercent = effectivePercent,
            IsPageCacheDominated = pageCacheDominated,
            Interpretation = interpretation,
        };
    }

    private static OomReport BuildOomReport(ContainerInspectResult inspect)
    {
        var name = inspect.DisplayName;
        var state = inspect.State;
        var oomKilled = state?.OomKilled == true;
        var exitCode = state?.ExitCode ?? 0;
        var restartCount = inspect.RestartCount;
        var finishedAt = state?.FinishedAt;
        var limit = ConfiguredMemoryLimit(inspect);
        var running = inspect.IsRunning;

        var limitClause = limit is null
            ? "no memory limit is set on this container"
            : $"its memory limit is {DiagnosticsFormatting.Bytes(limit.Value)}";
        var restartClause = restartCount > 0
            ? $" after {DiagnosticsFormatting.Count(restartCount)} restart(s)"
            : string.Empty;
        var whenClause = finishedAt is null
            ? string.Empty
            : $" at {DiagnosticsFormatting.Timestamp(finishedAt.Value)}";

        string interpretation;
        if (oomKilled)
        {
            interpretation =
                $"Container '{name}' was terminated by the kernel OOM killer (exit code " +
                $"{exitCode.ToString(CultureInfo.InvariantCulture)}){whenClause}{restartClause} and " +
                $"{limitClause}; raise ResourceLimits.MemoryBytes or fix the workload's memory growth.";
        }
        else if (running)
        {
            interpretation = restartCount > 0
                ? $"Container '{name}' is running and has not been OOM-killed, but the daemon has restarted it " +
                  $"{DiagnosticsFormatting.Count(restartCount)} time(s) — check the exit reason of the earlier runs."
                : $"Container '{name}' is running and has never been OOM-killed.";
        }
        else if (exitCode == 137)
        {
            interpretation =
                $"Container '{name}' exited with code 137 (SIGKILL){whenClause} but the daemon recorded no OOM " +
                "kill, so it was most likely stopped or killed from outside rather than by the memory limit.";
        }
        else if (exitCode == 0)
        {
            interpretation =
                $"Container '{name}' exited normally with code 0{whenClause} and was never OOM-killed.";
        }
        else
        {
            interpretation =
                $"Container '{name}' exited with code {exitCode.ToString(CultureInfo.InvariantCulture)}" +
                $"{whenClause} and was not OOM-killed; read the container logs for the failure.";
        }

        return new OomReport
        {
            ContainerName = name,
            IsRunning = running,
            WasOomKilled = oomKilled,
            ExitCode = exitCode,
            RestartCount = restartCount,
            FinishedAt = finishedAt,
            MemoryLimitBytes = limit,
            Interpretation = interpretation,
        };
    }

    private static HealthReport BuildHealthReport(ContainerInspectResult inspect)
    {
        var name = inspect.DisplayName;
        var hasHealthcheck = HasHealthcheck(inspect);
        var health = inspect.State?.Health;
        var logs = health?.Log ?? [];
        var status = health?.Status;

        string interpretation;
        if (!hasHealthcheck)
        {
            interpretation =
                $"Container '{name}' defines no healthcheck, so the daemon reports it as up as soon as its " +
                "process starts, whether or not it can actually serve — add ContainerSpec.Healthcheck.";
        }
        else if (string.Equals(status, "healthy", StringComparison.OrdinalIgnoreCase))
        {
            interpretation = $"Container '{name}' is passing its healthcheck.";
        }
        else if (string.Equals(status, "starting", StringComparison.OrdinalIgnoreCase))
        {
            interpretation =
                $"Container '{name}' is still inside its healthcheck start period, so it has not reported " +
                "healthy yet.";
        }
        else if (string.Equals(status, "unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            var lastOutput = LastOutput(logs);
            interpretation =
                $"Container '{name}' is unhealthy after " +
                $"{DiagnosticsFormatting.Count(health?.FailingStreak ?? 0)} consecutive failed probe(s)" +
                (lastOutput is null ? "." : $"; the last probe reported: {lastOutput}");
        }
        else
        {
            interpretation =
                $"Container '{name}' has a healthcheck but the daemon reports status '{status ?? "none"}', " +
                "which normally means the container is not running.";
        }

        return new HealthReport
        {
            ContainerName = name,
            HasHealthcheck = hasHealthcheck,
            Status = status,
            FailingStreak = health?.FailingStreak ?? 0,
            RecentLogs = logs,
            Interpretation = interpretation,
        };
    }

    private static string Summarize(ContainerInspectResult inspect, CpuThrottlingReport cpu,
        MemoryBreakdownReport memory, OomReport oom, HealthReport health)
    {
        if (oom.WasOomKilled)
        {
            return oom.Interpretation;
        }

        if (cpu.Severity >= ThrottleSeverity.High)
        {
            return cpu.Interpretation;
        }

        if (memory.EffectiveUsagePercent >= 90d)
        {
            return memory.Interpretation;
        }

        if (health.HasHealthcheck && string.Equals(health.Status, "unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            return health.Interpretation;
        }

        if (!inspect.IsRunning)
        {
            return oom.Interpretation;
        }

        return $"Container '{inspect.DisplayName}' shows no CPU throttling, memory pressure, OOM kill or health " +
               "failure worth acting on.";
    }

    private static bool IsPageCacheDominated(long usage, long? anon, long? file)
    {
        if (file is null || file.Value <= 0 || usage <= 0)
        {
            return false;
        }

        return file.Value > 2 * (anon ?? 0) && file.Value > usage / 2;
    }

    private static string NoLiveStatsSentence(ContainerInspectResult inspect, string what) =>
        $"Container '{inspect.DisplayName}' is '{inspect.State?.Status ?? "not running"}', so live {what} are " +
        "unavailable; start the container and sample it while it works.";

    private static string FormatCpus(double cpus) => cpus.ToString("0.##", CultureInfo.InvariantCulture);

    private static string LastOutput(IReadOnlyList<ContainerHealthLogEntry> logs)
    {
        for (var i = logs.Count - 1; i >= 0; i--)
        {
            var output = logs[i].Output;
            if (!string.IsNullOrWhiteSpace(output))
            {
                var trimmed = output.Trim().ReplaceLineEndings(" ");
                return trimmed.Length > 200 ? trimmed[..200] : trimmed;
            }
        }

        return null;
    }
}
