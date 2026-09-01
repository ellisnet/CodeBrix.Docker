namespace CodeBrix.Docker;

/// <summary>
/// One thing the advisor found wrong with a container, and what to change to fix it.
/// </summary>
/// <param name="RuleId">The stable rule identifier, for example <c>CB005</c>.</param>
/// <param name="Severity">How urgently the finding needs attention.</param>
/// <param name="ContainerName">The container name, without the daemon's leading slash.</param>
/// <param name="Title">A short headline, for example <c>No memory limit set</c>.</param>
/// <param name="Detail">What was observed, naming the actual values.</param>
/// <param name="Recommendation">
/// The concrete change to make, naming the library property or Docker flag to set.
/// </param>
public sealed record AdvisorFinding(
    string RuleId,
    AdvisorSeverity Severity,
    string ContainerName,
    string Title,
    string Detail,
    string Recommendation);
