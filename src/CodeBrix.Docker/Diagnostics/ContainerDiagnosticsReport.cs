namespace CodeBrix.Docker;

/// <summary>
/// Every diagnostic this library produces for one container, gathered in a single pass.
/// </summary>
public sealed class ContainerDiagnosticsReport
{
    /// <summary>Gets the full container id.</summary>
    public string ContainerId { get; init; } = string.Empty;

    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>Gets the lifecycle state, for example <c>running</c> or <c>exited</c>.</summary>
    public string Status { get; init; }

    /// <summary>Gets a value indicating whether the container was running when the report was taken.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the CPU-throttling diagnostic.</summary>
    public required CpuThrottlingReport CpuThrottling { get; init; }

    /// <summary>Gets the memory breakdown diagnostic.</summary>
    public required MemoryBreakdownReport Memory { get; init; }

    /// <summary>Gets the OOM-kill diagnostic.</summary>
    public required OomReport Oom { get; init; }

    /// <summary>Gets the healthcheck diagnostic.</summary>
    public required HealthReport Health { get; init; }

    /// <summary>
    /// Gets a short summary that leads with whatever is wrong — an OOM kill, heavy throttling, memory
    /// pressure or a failing healthcheck — and otherwise says the container looks healthy.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}
