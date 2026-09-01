using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// The outcome of a Hadolint lint of a Dockerfile.
/// </summary>
public sealed class HadolintResult
{
    /// <summary>Gets the full path of the Dockerfile that was linted.</summary>
    public required string DockerfilePath { get; init; }

    /// <summary>Gets every rule violation found, in the order Hadolint reported them.</summary>
    public required IReadOnlyList<HadolintFinding> Findings { get; init; }

    /// <summary>
    /// Gets the number of findings per level, keyed by Hadolint's lowercase level names
    /// (<c>style</c>, <c>info</c>, <c>warning</c>, <c>error</c>). Levels with no findings are absent.
    /// </summary>
    public required IReadOnlyDictionary<string, int> CountByLevel { get; init; }

    /// <summary>Gets the total number of findings.</summary>
    public int Total => Findings.Count;

    /// <summary>Gets the exit code of the Hadolint container.</summary>
    public long ExitCode { get; init; }
}
