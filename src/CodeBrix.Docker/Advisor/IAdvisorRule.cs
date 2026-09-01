namespace CodeBrix.Docker;

/// <summary>
/// One best-practice check the advisor runs against a container.
/// </summary>
internal interface IAdvisorRule
{
    /// <summary>Gets the stable rule identifier, for example <c>CB005</c>.</summary>
    string RuleId { get; }

    /// <summary>
    /// Evaluates the rule.
    /// </summary>
    /// <param name="context">The container's configuration and, when running, live statistics.</param>
    /// <returns>A finding, or <see langword="null"/> when the rule does not fire.</returns>
    AdvisorFinding Evaluate(AdvisorContext context);
}
