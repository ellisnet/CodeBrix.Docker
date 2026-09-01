using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>Request body for <c>POST /containers/create</c>.</summary>
internal sealed class ContainerCreateRequest
{
    [JsonPropertyName("Image")]
    public string Image { get; init; }

    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; }

    [JsonPropertyName("Entrypoint")]
    public IReadOnlyList<string> Entrypoint { get; init; }

    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; }

    [JsonPropertyName("Labels")]
    public IDictionary<string, string> Labels { get; init; }

    [JsonPropertyName("User")]
    public string User { get; init; }

    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; init; }

    [JsonPropertyName("Hostname")]
    public string Hostname { get; init; }

    [JsonPropertyName("ExposedPorts")]
    public IDictionary<string, JsonEmptyObject> ExposedPorts { get; init; }

    [JsonPropertyName("Healthcheck")]
    public HealthcheckSpec Healthcheck { get; init; }

    [JsonPropertyName("HostConfig")]
    public ContainerCreateHostConfig HostConfig { get; init; }

    [JsonPropertyName("NetworkingConfig")]
    public ContainerNetworkingConfig NetworkingConfig { get; init; }
}

/// <summary>The <c>HostConfig</c> section of a create request.</summary>
internal sealed class ContainerCreateHostConfig
{
    [JsonPropertyName("PortBindings")]
    public IDictionary<string, List<HostPortBinding>> PortBindings { get; init; }

    [JsonPropertyName("Mounts")]
    public IReadOnlyList<MountWire> Mounts { get; init; }

    [JsonPropertyName("RestartPolicy")]
    public HostRestartPolicy RestartPolicy { get; init; }

    [JsonPropertyName("AutoRemove")]
    public bool? AutoRemove { get; init; }

    [JsonPropertyName("Privileged")]
    public bool? Privileged { get; init; }

    [JsonPropertyName("LogConfig")]
    public LogConfig LogConfig { get; init; }

    [JsonPropertyName("NanoCpus")]
    public long? NanoCpus { get; init; }

    [JsonPropertyName("CpusetCpus")]
    public string CpusetCpus { get; init; }

    [JsonPropertyName("CpuShares")]
    public long? CpuShares { get; init; }

    [JsonPropertyName("Memory")]
    public long? Memory { get; init; }

    [JsonPropertyName("MemoryReservation")]
    public long? MemoryReservation { get; init; }

    [JsonPropertyName("MemorySwap")]
    public long? MemorySwap { get; init; }

    [JsonPropertyName("PidsLimit")]
    public long? PidsLimit { get; init; }
}

/// <summary>One published host port.</summary>
internal sealed class HostPortBinding
{
    [JsonPropertyName("HostIp")]
    public string HostIp { get; init; }

    [JsonPropertyName("HostPort")]
    public string HostPort { get; init; }
}

/// <summary>A mount in the daemon's create-request shape.</summary>
internal sealed class MountWire
{
    [JsonPropertyName("Type")]
    public string Type { get; init; }

    [JsonPropertyName("Source")]
    public string Source { get; init; }

    [JsonPropertyName("Target")]
    public string Target { get; init; }

    [JsonPropertyName("ReadOnly")]
    public bool ReadOnly { get; init; }

    [JsonPropertyName("TmpfsOptions")]
    public TmpfsOptionsWire TmpfsOptions { get; init; }
}

/// <summary>Options for a tmpfs mount.</summary>
internal sealed class TmpfsOptionsWire
{
    [JsonPropertyName("SizeBytes")]
    public long? SizeBytes { get; init; }
}

/// <summary>The <c>NetworkingConfig</c> section of a create request.</summary>
internal sealed class ContainerNetworkingConfig
{
    [JsonPropertyName("EndpointsConfig")]
    public IDictionary<string, EndpointConfigWire> EndpointsConfig { get; init; }
}

/// <summary>One network attachment in a create request.</summary>
internal sealed class EndpointConfigWire
{
    [JsonPropertyName("Aliases")]
    public IReadOnlyList<string> Aliases { get; init; }
}

/// <summary>Response body of <c>POST /containers/create</c>.</summary>
internal sealed class ContainerCreateResponse
{
    [JsonPropertyName("Id")]
    public string Id { get; init; }

    [JsonPropertyName("Warnings")]
    public IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Request body of <c>POST /containers/{id}/update</c>.</summary>
internal sealed class ContainerUpdateRequest
{
    [JsonPropertyName("NanoCpus")]
    public long? NanoCpus { get; init; }

    [JsonPropertyName("CpusetCpus")]
    public string CpusetCpus { get; init; }

    [JsonPropertyName("CpuShares")]
    public long? CpuShares { get; init; }

    [JsonPropertyName("Memory")]
    public long? Memory { get; init; }

    [JsonPropertyName("MemoryReservation")]
    public long? MemoryReservation { get; init; }

    [JsonPropertyName("MemorySwap")]
    public long? MemorySwap { get; init; }

    [JsonPropertyName("PidsLimit")]
    public long? PidsLimit { get; init; }
}

/// <summary>Response body of <c>POST /containers/{id}/wait</c>.</summary>
internal sealed class ContainerWaitResponse
{
    [JsonPropertyName("StatusCode")]
    public long StatusCode { get; init; }

    [JsonPropertyName("Error")]
    public ContainerWaitError Error { get; init; }
}

/// <summary>The error payload of a wait response.</summary>
internal sealed class ContainerWaitError
{
    [JsonPropertyName("Message")]
    public string Message { get; init; }
}

/// <summary>Request body of <c>POST /containers/{id}/exec</c>.</summary>
internal sealed class ExecCreateRequest
{
    [JsonPropertyName("AttachStdin")]
    public bool AttachStdin { get; init; }

    [JsonPropertyName("AttachStdout")]
    public bool AttachStdout { get; init; } = true;

    [JsonPropertyName("AttachStderr")]
    public bool AttachStderr { get; init; } = true;

    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }

    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; }

    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; }

    [JsonPropertyName("User")]
    public string User { get; init; }

    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; init; }

    [JsonPropertyName("Privileged")]
    public bool Privileged { get; init; }
}

/// <summary>Response body of <c>POST /containers/{id}/exec</c>.</summary>
internal sealed class ExecCreateResponse
{
    [JsonPropertyName("Id")]
    public string Id { get; init; }
}

/// <summary>Request body of <c>POST /exec/{id}/start</c>.</summary>
internal sealed class ExecStartRequest
{
    [JsonPropertyName("Detach")]
    public bool Detach { get; init; }

    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }

    /// <summary>The initial terminal size as <c>[height, width]</c>, omitted when not requested.</summary>
    [JsonPropertyName("ConsoleSize")]
    public IReadOnlyList<int> ConsoleSize { get; init; }
}

/// <summary>Response body of <c>POST /containers/prune</c>.</summary>
internal sealed class ContainersPruneResponse
{
    [JsonPropertyName("ContainersDeleted")]
    public IReadOnlyList<string> ContainersDeleted { get; init; }

    [JsonPropertyName("SpaceReclaimed")]
    public long SpaceReclaimed { get; init; }
}
