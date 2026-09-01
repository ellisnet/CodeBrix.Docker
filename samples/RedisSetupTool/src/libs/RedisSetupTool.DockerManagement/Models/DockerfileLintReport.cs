using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>The outcome of linting one Dockerfile.</summary>
public sealed class DockerfileLintReport
{
    /// <summary>Gets the file that was linted.</summary>
    public string DockerfilePath { get; init; }

    /// <summary>Gets the number of findings.</summary>
    public int Total { get; init; }

    /// <summary>Gets the finding count per level; never null.</summary>
    public IReadOnlyDictionary<string, int> CountByLevel { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Gets the findings; never null.</summary>
    public IReadOnlyList<LintFindingInfo> Findings { get; init; } = [];
}
