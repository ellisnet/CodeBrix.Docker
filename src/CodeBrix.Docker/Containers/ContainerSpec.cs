using System;
using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// Everything needed to create a container. Only <see cref="Image"/> is required.
/// </summary>
/// <example>
/// <code>
/// var spec = new ContainerSpec
/// {
///     Image = "alpine:latest",
///     Command = ["sh", "-c", "while :; do :; done"],
///     Labels = { ["codebrix.docker.tests"] = "true" },
///     Limits = new ResourceLimits { Cpus = 0.25, MemoryBytes = ResourceLimits.Megabytes(64) },
/// };
/// </code>
/// </example>
public sealed class ContainerSpec
{
    /// <summary>Gets or sets the image reference to run, for example <c>alpine:latest</c>. Required.</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>Gets or sets the container name. When omitted the daemon generates one.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the command, overriding the image's <c>CMD</c>.</summary>
    public IReadOnlyList<string> Command { get; set; }

    /// <summary>Gets or sets the entrypoint, overriding the image's <c>ENTRYPOINT</c>.</summary>
    public IReadOnlyList<string> Entrypoint { get; set; }

    /// <summary>Gets or sets the environment variables, each in <c>KEY=VALUE</c> form.</summary>
    public IList<string> Env { get; set; } = [];

    /// <summary>Gets or sets the labels to attach to the container.</summary>
    public IDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the user to run as, for example <c>1000:1000</c> or <c>appuser</c>.</summary>
    public string User { get; set; }

    /// <summary>Gets or sets the working directory inside the container.</summary>
    public string WorkingDir { get; set; }

    /// <summary>Gets or sets the container host name.</summary>
    public string HostName { get; set; }

    /// <summary>
    /// Gets or sets the ports to expose and, where <see cref="PortBinding.HostPort"/> is set, publish.
    /// </summary>
    public IList<PortBinding> PortBindings { get; set; } = [];

    /// <summary>
    /// Gets or sets ports to expose without publishing them. Ports named in
    /// <see cref="PortBindings"/> are exposed automatically.
    /// </summary>
    public IList<PortBinding> ExposedPorts { get; set; } = [];

    /// <summary>Gets or sets the volume, bind and tmpfs mounts.</summary>
    public IList<MountSpec> Mounts { get; set; } = [];

    /// <summary>Gets or sets the user-defined network to attach the container to at creation time.</summary>
    public string NetworkName { get; set; }

    /// <summary>
    /// Gets or sets additional DNS names the container answers to on <see cref="NetworkName"/>.
    /// </summary>
    public IList<string> NetworkAliases { get; set; } = [];

    /// <summary>Gets or sets the restart policy. When omitted the daemon default (<c>no</c>) applies.</summary>
    public RestartPolicy RestartPolicy { get; set; }

    /// <summary>Gets or sets a value indicating whether the daemon removes the container when it exits.</summary>
    public bool AutoRemove { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the container runs privileged. This disables most
    /// isolation and should be avoided.
    /// </summary>
    public bool Privileged { get; set; }

    /// <summary>Gets or sets the healthcheck, overriding the image's own.</summary>
    public HealthcheckSpec Healthcheck { get; set; }

    /// <summary>Gets or sets the logging driver, for example <c>json-file</c> or <c>local</c>.</summary>
    public string LogDriver { get; set; }

    /// <summary>
    /// Gets or sets logging driver options, for example <c>max-size</c> and <c>max-file</c> for
    /// <c>json-file</c>. Without these, container logs grow without bound.
    /// </summary>
    public IDictionary<string, string> LogOptions { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the cgroup resource limits.</summary>
    public ResourceLimits Limits { get; set; }
}
