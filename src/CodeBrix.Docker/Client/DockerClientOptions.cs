using System;

namespace CodeBrix.Docker;

/// <summary>
/// Configuration for a <see cref="DockerClient"/>.
/// </summary>
public sealed class DockerClientOptions
{
    /// <summary>
    /// Gets or sets the daemon endpoint, for example <c>npipe://./pipe/docker_engine</c>,
    /// <c>unix:///var/run/docker.sock</c> or <c>tcp://127.0.0.1:2375</c>.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> the endpoint is resolved from the <c>DOCKER_HOST</c> environment
    /// variable, falling back to the platform default.
    /// </remarks>
    public string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the <c>docker</c> executable used for the few operations that require the CLI
    /// (BuildKit image builds and authenticated pulls that need credential helpers).
    /// </summary>
    public string DockerCliPath { get; set; } = "docker";

    /// <summary>
    /// Gets or sets the timeout applied to each non-streaming Engine API call.
    /// Streaming calls (logs, stats streams, events, waits) are never timed out.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);
}
