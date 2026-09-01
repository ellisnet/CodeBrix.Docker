namespace CodeBrix.Docker;

/// <summary>
/// Everything the advisor knows about one container: its configuration, and — when it is running —
/// one live statistics sample.
/// </summary>
internal sealed class AdvisorContext
{
    public AdvisorContext(ContainerInspectResult inspect, ContainerStats stats)
    {
        Inspect = inspect;
        Stats = stats is { HasLiveData: true } ? stats : null;
    }

    /// <summary>Gets the container's inspect payload.</summary>
    public ContainerInspectResult Inspect { get; }

    /// <summary>
    /// Gets a live statistics sample, or <see langword="null"/> when the container is not running or
    /// the daemon returned the empty counters it gives for stopped containers.
    /// </summary>
    public ContainerStats Stats { get; }

    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName => Inspect.DisplayName;

    /// <summary>Gets the container's host configuration, when the daemon reported one.</summary>
    public ContainerHostConfig HostConfig => Inspect.HostConfig;

    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning => Inspect.IsRunning;

    /// <summary>Gets a value indicating whether a live statistics sample is available.</summary>
    public bool HasLiveStats => Stats is not null;

    /// <summary>Gets the configured hard memory limit in bytes, or <see langword="null"/> when unlimited.</summary>
    public long? MemoryLimitBytes => DiagnosticsOperations.ConfiguredMemoryLimit(Inspect);
}
