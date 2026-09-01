namespace CodeBrix.Docker;

/// <summary>
/// Publishes a container port on the host.
/// </summary>
/// <param name="ContainerPort">The port inside the container.</param>
/// <param name="HostPort">The host port, or <see langword="null"/> to let the daemon pick a free one.</param>
/// <param name="Protocol">The protocol, <c>tcp</c> (default) or <c>udp</c>.</param>
public sealed record PortBinding(int ContainerPort, int? HostPort = null, string Protocol = "tcp")
{
    /// <summary>
    /// Gets the port in the daemon's <c>&lt;port&gt;/&lt;protocol&gt;</c> notation, for example <c>8080/tcp</c>.
    /// </summary>
    public string PortKey =>
        $"{ContainerPort}/{(string.IsNullOrWhiteSpace(Protocol) ? "tcp" : Protocol.ToLowerInvariant())}";
}
