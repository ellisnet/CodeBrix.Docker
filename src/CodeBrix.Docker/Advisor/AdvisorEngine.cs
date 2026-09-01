using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// The optimization advisor: evaluates container configuration and live statistics against the
/// library's rule set.
/// </summary>
/// <remarks>
/// Every rule produces an <see cref="AdvisorFinding"/> that names the observed values and the concrete
/// property or flag to change. Configuration rules apply to any container, running or not; the rules
/// that need live counters (CPU throttling, memory pressure, page-cache dominance) are skipped for
/// containers that are not running.
/// </remarks>
/// <example>
/// <code>
/// using var docker = DockerClient.Create();
/// foreach (var finding in await docker.Advisor.AnalyzeContainerAsync("web"))
/// {
///     Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Title}");
/// }
/// </code>
/// </example>
public sealed class AdvisorEngine
{
    private static readonly IAdvisorRule[] Rules =
    [
        new NoMemoryLimitRule(),
        new SwapNotDisabledRule(),
        new NoPidsLimitRule(),
        new NoCpuLimitRule(),
        new CpuThrottlingRule(),
        new MemoryPressureRule(),
        new NoHealthcheckRule(),
        new RunningAsRootRule(),
        new NoMemoryReservationRule(),
        new PrivilegedContainerRule(),
        new UnboundedLogsRule(),
        new PreviousOomKillRule(),
        new PageCacheDominatedMemoryRule(),
        new MutableImageTagRule(),
    ];

    private readonly ContainerOperations _containers;

    internal AdvisorEngine(DockerApiClient api) => _containers = new ContainerOperations(api);

    /// <summary>
    /// Gets the identifiers of every rule this advisor evaluates, in evaluation order.
    /// </summary>
    public static IReadOnlyList<string> RuleIds { get; } = Rules.Select(r => r.RuleId).ToArray();

    /// <summary>
    /// Analyzes one container: its configuration and, when it is running, a live statistics sample.
    /// </summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The findings, ordered by severity (most severe first) and then by rule id.</returns>
    /// <exception cref="DockerContainerNotFoundException">No such container.</exception>
    public async Task<IReadOnlyList<AdvisorFinding>> AnalyzeContainerAsync(string idOrName,
        CancellationToken cancellationToken = default)
    {
        var inspect = await _containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false);
        var stats = await TryGetStatsAsync(inspect, cancellationToken).ConfigureAwait(false);
        return Evaluate(new AdvisorContext(inspect, stats));
    }

    /// <summary>
    /// Analyzes every container on the daemon, including stopped ones.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The findings for all containers, ordered by container name, then severity (most severe first),
    /// then rule id. Containers that disappear mid-analysis are skipped.
    /// </returns>
    public async Task<IReadOnlyList<AdvisorFinding>> AnalyzeAllContainersAsync(
        CancellationToken cancellationToken = default)
    {
        var containers = await _containers.ListAsync(all: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var findings = new List<AdvisorFinding>();
        foreach (var container in containers.OrderBy(c => c.DisplayName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ContainerInspectResult inspect;
            try
            {
                inspect = await _containers.InspectAsync(container.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerContainerNotFoundException)
            {
                continue;
            }

            var stats = await TryGetStatsAsync(inspect, cancellationToken).ConfigureAwait(false);
            findings.AddRange(Evaluate(new AdvisorContext(inspect, stats)));
        }

        return findings;
    }

    private static IReadOnlyList<AdvisorFinding> Evaluate(AdvisorContext context)
    {
        var findings = new List<AdvisorFinding>();
        foreach (var rule in Rules)
        {
            var finding = rule.Evaluate(context);
            if (finding is not null)
            {
                findings.Add(finding);
            }
        }

        findings.Sort(static (left, right) =>
        {
            var bySeverity = right.Severity.CompareTo(left.Severity);
            return bySeverity != 0 ? bySeverity : string.CompareOrdinal(left.RuleId, right.RuleId);
        });

        return findings;
    }

    private async Task<ContainerStats> TryGetStatsAsync(ContainerInspectResult inspect,
        CancellationToken cancellationToken)
    {
        if (!inspect.IsRunning)
        {
            return null;
        }

        try
        {
            return await _containers.GetStatsAsync(inspect.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerApiException)
        {
            // The container stopped between the inspect and the sample; the configuration rules still apply.
            return null;
        }
    }
}
