using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The state of an exec instance, from <c>GET /exec/{id}/json</c>. This is where a streaming exec
/// session's exit code comes from — the hijacked stream itself carries no status.
/// </summary>
public sealed class ExecInspectResult
{
    /// <summary>Gets the exec instance's id.</summary>
    [JsonPropertyName("ID")]
    public string Id { get; init; }

    /// <summary>Gets a value indicating whether the command is still running.</summary>
    [JsonPropertyName("Running")]
    public bool Running { get; init; }

    /// <summary>
    /// Gets the exit code, which is only meaningful once <see cref="Running"/> is
    /// <see langword="false"/>.
    /// </summary>
    [JsonPropertyName("ExitCode")]
    public long? ExitCode { get; init; }

    /// <summary>Gets the id of the container the command runs in.</summary>
    [JsonPropertyName("ContainerID")]
    public string ContainerId { get; init; }

    /// <summary>Gets the process id inside the container, or zero once the command has exited.</summary>
    [JsonPropertyName("Pid")]
    public long Pid { get; init; }

    /// <summary>Gets a value indicating whether standard input was attached.</summary>
    [JsonPropertyName("OpenStdin")]
    public bool OpenStdin { get; init; }

    /// <summary>Gets a value indicating whether standard output was attached.</summary>
    [JsonPropertyName("OpenStdout")]
    public bool OpenStdout { get; init; }

    /// <summary>Gets a value indicating whether standard error was attached.</summary>
    [JsonPropertyName("OpenStderr")]
    public bool OpenStderr { get; init; }

    /// <summary>Gets a value indicating whether the command has finished.</summary>
    [JsonIgnore]
    public bool HasExited => !Running;
}
