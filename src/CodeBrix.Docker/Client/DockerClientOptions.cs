using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Configuration for a <see cref="DockerClient"/>.
/// </summary>
public sealed class DockerClientOptions
{
    /// <summary>
    /// Gets or sets the daemon endpoint, for example <c>npipe://./pipe/docker_engine</c>,
    /// <c>unix:///var/run/docker.sock</c>, <c>tcp://127.0.0.1:2375</c> or
    /// <c>ssh://user@host:2222</c>.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> the endpoint is resolved from the <c>DOCKER_HOST</c> environment
    /// variable, falling back to the platform default. TLS-secured endpoints (<c>https://</c>) are not
    /// supported; reach a remote daemon over <c>ssh://</c> instead.
    /// </remarks>
    public string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the <c>docker</c> executable used for the few operations that require the CLI
    /// (BuildKit image builds and authenticated pulls that need credential helpers).
    /// </summary>
    /// <remarks>
    /// When <see cref="Endpoint"/> is set, it is passed to that executable as <c>DOCKER_HOST</c>, so
    /// CLI-backed operations act on the same daemon as the rest of the client.
    /// </remarks>
    public string DockerCliPath { get; set; } = "docker";

    /// <summary>
    /// Gets or sets the SSH client used by <c>ssh://</c> endpoints. The default is the <c>ssh</c> on
    /// <c>PATH</c>: OpenSSH, which ships with Linux, macOS and Windows 10 and later.
    /// </summary>
    public string SshExecutablePath { get; set; } = "ssh";

    /// <summary>
    /// Gets or sets extra arguments handed to the SSH client for <c>ssh://</c> endpoints, inserted
    /// after CodeBrix.Docker's own options and before the destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this for the things <c>~/.ssh/config</c> would normally carry when a service cannot rely on
    /// one: <c>["-i", "/path/to/key"]</c>, <c>["-J", "bastion"]</c>, or
    /// <c>["-o", "UserKnownHostsFile=/etc/docker/known_hosts"]</c>.
    /// </para>
    /// <para>
    /// CodeBrix.Docker always passes <c>-o BatchMode=yes</c> first, and OpenSSH honours the first value
    /// it is given for an option, so a password prompt can never be reintroduced here. Host-key checking
    /// is left entirely to OpenSSH: nothing is ever accepted automatically, and turning that off with
    /// <c>StrictHostKeyChecking=no</c> is a real security downgrade rather than a convenience.
    /// </para>
    /// </remarks>
    public IList<string> SshArguments { get; set; } = [];

    /// <summary>
    /// Gets or sets the timeout applied to each non-streaming Engine API call.
    /// Streaming calls (logs, stats streams, events, waits) are never timed out.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);
}
