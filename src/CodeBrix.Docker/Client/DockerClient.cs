using System;

namespace CodeBrix.Docker;

/// <summary>
/// The entry point of CodeBrix.Docker: a connection to a Docker daemon plus the operation groups
/// that act on it.
/// </summary>
/// <example>
/// <code>
/// using var docker = DockerClient.Create();
/// var info = await docker.System.GetInfoAsync();
/// </code>
/// </example>
public sealed class DockerClient : IDisposable
{
    private readonly DockerApiClient _api;
    private bool _disposed;

    private DockerClient(DockerClientOptions options)
    {
        _api = new DockerApiClient(options);

        Containers = new ContainerOperations(_api);
        Images = new ImageOperations(_api, options);
        Networks = new NetworkOperations(_api);
        Volumes = new VolumeOperations(_api);
        System = new SystemOperations(_api);
        Diagnostics = new DiagnosticsOperations(_api);
        Advisor = new AdvisorEngine(_api);
        Analysis = new AnalysisOperations(_api);
    }

    /// <summary>
    /// Creates a client for the resolved daemon endpoint.
    /// </summary>
    /// <param name="options">
    /// Optional configuration. When omitted, the endpoint is taken from <c>DOCKER_HOST</c> or the
    /// platform default (<c>npipe://./pipe/docker_engine</c> on Windows,
    /// <c>unix:///var/run/docker.sock</c> elsewhere).
    /// </param>
    /// <returns>A new client. Dispose it when finished.</returns>
    /// <exception cref="DockerException">The configured endpoint is malformed.</exception>
    /// <exception cref="NotSupportedException">The endpoint scheme is not supported.</exception>
    public static DockerClient Create(DockerClientOptions options = null) =>
        new(options ?? new DockerClientOptions());

    /// <summary>Gets the endpoint string this client is connected to.</summary>
    public string Endpoint => _api.Endpoint.Original;

    /// <summary>Gets the container lifecycle, resource and inspection operations.</summary>
    public ContainerOperations Containers { get; }

    /// <summary>Gets the image lifecycle and build operations.</summary>
    public ImageOperations Images { get; }

    /// <summary>Gets the network operations.</summary>
    public NetworkOperations Networks { get; }

    /// <summary>Gets the volume operations.</summary>
    public VolumeOperations Volumes { get; }

    /// <summary>Gets the daemon-level operations: ping, version, info, disk usage and events.</summary>
    public SystemOperations System { get; }

    /// <summary>Gets the diagnostics operations: CPU throttling, memory breakdown, OOM and health.</summary>
    public DiagnosticsOperations Diagnostics { get; }

    /// <summary>Gets the optimization advisor.</summary>
    public AdvisorEngine Advisor { get; }

    /// <summary>Gets the containerized image-analysis operations (Trivy, Dive, Hadolint, Slim).</summary>
    public AnalysisOperations Analysis { get; }

    /// <summary>
    /// Releases the underlying HTTP connection to the daemon.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _api.Dispose();
    }
}
