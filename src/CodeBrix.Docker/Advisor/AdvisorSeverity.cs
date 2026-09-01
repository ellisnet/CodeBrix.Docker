namespace CodeBrix.Docker;

/// <summary>
/// How urgently an advisor finding needs attention.
/// </summary>
public enum AdvisorSeverity
{
    /// <summary>Worth knowing: a tuning or reproducibility improvement, not a risk.</summary>
    Info = 0,

    /// <summary>A real operational risk — resource exhaustion, unpredictable performance or weak isolation.</summary>
    Warning = 1,

    /// <summary>Actively dangerous or already failing in production.</summary>
    Critical = 2,
}
