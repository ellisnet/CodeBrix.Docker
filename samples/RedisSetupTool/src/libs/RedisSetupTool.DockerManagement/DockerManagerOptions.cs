using System;

namespace RedisSetupTool.DockerManagement;

/// <summary>Options for the Docker facade. Defaults target the local daemon.</summary>
public sealed class DockerManagerOptions
{
    /// <summary>Gets or sets the daemon endpoint; null selects the platform default.</summary>
    public string Endpoint { get; set; }

    /// <summary>Gets or sets the path of the Docker CLI used by the analysis tier.</summary>
    public string DockerCliPath { get; set; } = "docker";

    /// <summary>Gets or sets the default request timeout.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(100);
}
