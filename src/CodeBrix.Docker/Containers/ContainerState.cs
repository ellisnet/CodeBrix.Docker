using System;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The runtime state of a container, from <c>State</c> in the inspect payload.
/// </summary>
public sealed class ContainerState
{
    /// <summary>
    /// Gets the state: <c>created</c>, <c>running</c>, <c>paused</c>, <c>restarting</c>,
    /// <c>removing</c>, <c>exited</c> or <c>dead</c>.
    /// </summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; }

    /// <summary>Gets a value indicating whether the container is running.</summary>
    [JsonPropertyName("Running")]
    public bool Running { get; init; }

    /// <summary>Gets a value indicating whether the container is paused.</summary>
    [JsonPropertyName("Paused")]
    public bool Paused { get; init; }

    /// <summary>Gets a value indicating whether the container is restarting.</summary>
    [JsonPropertyName("Restarting")]
    public bool Restarting { get; init; }

    /// <summary>
    /// Gets a value indicating whether the kernel OOM killer terminated the container. Paired with
    /// <see cref="ExitCode"/> 137, this is the definitive signal that a memory limit was too low.
    /// </summary>
    [JsonPropertyName("OOMKilled")]
    public bool OomKilled { get; init; }

    /// <summary>Gets a value indicating whether the container is dead (its removal failed).</summary>
    [JsonPropertyName("Dead")]
    public bool Dead { get; init; }

    /// <summary>Gets the host PID of the container's main process, or <c>0</c> when not running.</summary>
    [JsonPropertyName("Pid")]
    public long Pid { get; init; }

    /// <summary>Gets the exit code of the last run. <c>137</c> means SIGKILL, typically an OOM kill.</summary>
    [JsonPropertyName("ExitCode")]
    public long ExitCode { get; init; }

    /// <summary>Gets the daemon's error message for the last run, when there was one.</summary>
    [JsonPropertyName("Error")]
    public string Error { get; init; }

    /// <summary>Gets when the container last started, or <see langword="null"/> when it never has.</summary>
    [JsonPropertyName("StartedAt")]
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the container last exited, or <see langword="null"/> when it has not.</summary>
    [JsonPropertyName("FinishedAt")]
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets the healthcheck state, when the container has a healthcheck.</summary>
    [JsonPropertyName("Health")]
    public ContainerHealth Health { get; init; }
}
