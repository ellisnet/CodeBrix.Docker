namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One thing the advisor found wrong with a container.</summary>
public sealed class AdvisorFindingInfo
{
    /// <summary>Gets the stable rule id, for example <c>CB005</c>.</summary>
    public string RuleId { get; init; }

    /// <summary>Gets how urgently the finding needs attention.</summary>
    public AdvisorLevel Severity { get; init; }

    /// <summary>Gets the container name.</summary>
    public string ContainerName { get; init; }

    /// <summary>Gets a short headline.</summary>
    public string Title { get; init; }

    /// <summary>Gets what was observed, naming the actual values.</summary>
    public string Detail { get; init; }

    /// <summary>Gets the concrete change to make.</summary>
    public string Recommendation { get; init; }
}
