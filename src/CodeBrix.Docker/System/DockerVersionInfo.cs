using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// Version information reported by <c>GET /version</c>.
/// </summary>
public sealed class DockerVersionInfo
{
    /// <summary>Gets the daemon version, for example <c>29.7.2</c>.</summary>
    [JsonPropertyName("Version")]
    public string Version { get; init; }

    /// <summary>Gets the highest Engine API version the daemon supports, for example <c>1.55</c>.</summary>
    [JsonPropertyName("ApiVersion")]
    public string ApiVersion { get; init; }

    /// <summary>Gets the lowest Engine API version the daemon still accepts.</summary>
    [JsonPropertyName("MinAPIVersion")]
    public string MinApiVersion { get; init; }

    /// <summary>Gets the operating system the daemon runs on, for example <c>linux</c>.</summary>
    [JsonPropertyName("Os")]
    public string Os { get; init; }

    /// <summary>Gets the daemon's CPU architecture, for example <c>amd64</c>.</summary>
    [JsonPropertyName("Arch")]
    public string Arch { get; init; }

    /// <summary>Gets the kernel version the daemon runs on.</summary>
    [JsonPropertyName("KernelVersion")]
    public string KernelVersion { get; init; }

    /// <summary>Gets the Git commit the daemon was built from.</summary>
    [JsonPropertyName("GitCommit")]
    public string GitCommit { get; init; }

    /// <summary>Gets the Go version the daemon was built with.</summary>
    [JsonPropertyName("GoVersion")]
    public string GoVersion { get; init; }

    /// <summary>Gets the daemon build timestamp as reported by the API.</summary>
    [JsonPropertyName("BuildTime")]
    public string BuildTime { get; init; }

    /// <summary>Gets a value indicating whether experimental daemon features are enabled.</summary>
    [JsonPropertyName("Experimental")]
    public bool Experimental { get; init; }
}
