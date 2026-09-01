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

    /// <summary>
    /// A remote daemon reached through the system SSH client (<c>ssh://</c>), which runs
    /// <c>docker system dial-stdio</c> on the far end.
    /// </summary>
    Ssh,
}
