namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One published or exposed container port.</summary>
public sealed class PortMapping
{
    /// <summary>Gets the port inside the container.</summary>
    public int ContainerPort { get; init; }

    /// <summary>Gets the host port, when the port is published.</summary>
    public int? HostPort { get; init; }

    /// <summary>Gets the protocol, normally <c>tcp</c>.</summary>
    public string Protocol { get; init; }

    /// <summary>Gets the host address the port is bound to.</summary>
    public string HostIp { get; init; }

    /// <summary>Gets a display string such as <c>6379/tcp -&gt; 6401</c>.</summary>
    public string Display { get; init; }
}
