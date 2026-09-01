namespace CodeBrix.Docker;

/// <summary>
/// The transports CodeBrix.Docker can use to reach a Docker daemon.
/// </summary>
internal enum DockerEndpointKind
{
    /// <summary>A Windows named pipe (<c>npipe://</c>).</summary>
    NamedPipe,

    /// <summary>A Unix domain socket (<c>unix://</c>).</summary>
    UnixSocket,

    /// <summary>A plain TCP connection (<c>tcp://</c> or <c>http://</c>).</summary>
    Tcp,
}
