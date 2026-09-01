using System;
using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>Everything an inspect returns about one container.</summary>
public sealed class ContainerDetail
{
    /// <summary>Gets the full container id.</summary>
    public string Id { get; init; }

    /// <summary>Gets the first twelve characters of the id.</summary>
    public string ShortId { get; init; }

    /// <summary>Gets the container name, without the daemon's leading slash.</summary>
    public string Name { get; init; }

    /// <summary>Gets the image reference.</summary>
    public string Image { get; init; }

    /// <summary>Gets when the container was created.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>Gets the state word, for example <c>running</c>.</summary>
    public string StateStatus { get; init; }

    /// <summary>Gets a value indicating whether the container is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets a value indicating whether the container is paused.</summary>
    public bool IsPaused { get; init; }

    /// <summary>Gets a value indicating whether the container is restarting.</summary>
    public bool IsRestarting { get; init; }

    /// <summary>Gets a value indicating whether the container was killed by the OOM killer.</summary>
    public bool WasOomKilled { get; init; }

    /// <summary>Gets a value indicating whether the container is dead.</summary>
    public bool IsDead { get; init; }

    /// <summary>Gets the main process id, when the container is running.</summary>
    public long Pid { get; init; }

    /// <summary>Gets the last exit code.</summary>
    public long ExitCode { get; init; }

    /// <summary>Gets the last error the daemon recorded.</summary>
    public string Error { get; init; }

    /// <summary>Gets when the container last started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Gets when the container last stopped.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Gets how many times the container has been restarted.</summary>
    public long RestartCount { get; init; }

    /// <summary>Gets the healthcheck status, when the image declares one.</summary>
    public string HealthStatus { get; init; }

    /// <summary>Gets the number of consecutive healthcheck failures.</summary>
    public long HealthFailingStreak { get; init; }

    /// <summary>Gets a value indicating whether the healthcheck currently passes.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Gets the command; never null.</summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    /// <summary>Gets the entrypoint; never null.</summary>
    public IReadOnlyList<string> Entrypoint { get; init; } = [];

    /// <summary>Gets the environment variables; never null.</summary>
    public IReadOnlyList<string> Env { get; init; } = [];

    /// <summary>Gets the labels; never null.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the working directory.</summary>
    public string WorkingDir { get; init; }

    /// <summary>Gets the user the process runs as.</summary>
    public string User { get; init; }

    /// <summary>Gets the container hostname.</summary>
    public string Hostname { get; init; }

    /// <summary>Gets the path of the container's log file on the host.</summary>
    public string LogPath { get; init; }

    /// <summary>Gets the network mode.</summary>
    public string NetworkMode { get; init; }

    /// <summary>Gets the network attachments; never null.</summary>
    public IReadOnlyList<ContainerNetworkAttachment> Networks { get; init; } = [];

    /// <summary>Gets the mounts; never null.</summary>
    public IReadOnlyList<MountInfo> Mounts { get; init; } = [];

    /// <summary>Gets the resource limits the container was created with.</summary>
    public ResourceLimitInfo Limits { get; init; }
}
