namespace CodeBrix.Docker;

/// <summary>
/// The restart policy to apply to a new container.
/// </summary>
/// <param name="Kind">The policy kind.</param>
/// <param name="MaxRetries">
/// The retry cap, honoured only by <see cref="RestartPolicyKind.OnFailure"/>. Zero means unlimited.
/// </param>
public sealed record RestartPolicy(RestartPolicyKind Kind, int MaxRetries = 0)
{
    /// <summary>Never restart the container.</summary>
    public static RestartPolicy No { get; } = new(RestartPolicyKind.No);

    /// <summary>Always restart the container.</summary>
    public static RestartPolicy Always { get; } = new(RestartPolicyKind.Always);

    /// <summary>Always restart the container unless it was stopped explicitly.</summary>
    public static RestartPolicy UnlessStopped { get; } = new(RestartPolicyKind.UnlessStopped);

    /// <summary>
    /// Restarts the container when it exits non-zero, at most <paramref name="maxRetries"/> times.
    /// </summary>
    /// <param name="maxRetries">The retry cap; zero means unlimited.</param>
    /// <returns>The restart policy.</returns>
    public static RestartPolicy OnFailure(int maxRetries = 0) => new(RestartPolicyKind.OnFailure, maxRetries);

    /// <summary>Gets the daemon's wire name for <see cref="Kind"/>.</summary>
    internal string Name => Kind switch
    {
        RestartPolicyKind.Always => "always",
        RestartPolicyKind.OnFailure => "on-failure",
        RestartPolicyKind.UnlessStopped => "unless-stopped",
        _ => "no",
    };
}
