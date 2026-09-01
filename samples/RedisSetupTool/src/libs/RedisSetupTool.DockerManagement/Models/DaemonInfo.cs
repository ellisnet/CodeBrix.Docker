using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>What the daemon reports about itself.</summary>
public sealed class DaemonInfo
{
    /// <summary>Gets a value indicating whether the daemon answered a ping.</summary>
    public bool IsReachable { get; init; }

    /// <summary>Gets the endpoint the client is talking to.</summary>
    public string Endpoint { get; init; }

    /// <summary>Gets the daemon version, for example <c>29.7.2</c>.</summary>
    public string ServerVersion { get; init; }

    /// <summary>Gets the API version, for example <c>1.55</c>.</summary>
    public string ApiVersion { get; init; }

    /// <summary>Gets the oldest API version the daemon still serves.</summary>
    public string MinApiVersion { get; init; }

    /// <summary>Gets the daemon OS type, for example <c>linux</c>.</summary>
    public string OsType { get; init; }

    /// <summary>Gets the host operating system description.</summary>
    public string OperatingSystem { get; init; }

    /// <summary>Gets the host kernel version.</summary>
    public string KernelVersion { get; init; }

    /// <summary>Gets the host architecture.</summary>
    public string Architecture { get; init; }

    /// <summary>Gets the cgroup version in use.</summary>
    public string CgroupVersion { get; init; }

    /// <summary>Gets the cgroup driver in use.</summary>
    public string CgroupDriver { get; init; }

    /// <summary>Gets the storage driver in use.</summary>
    public string StorageDriver { get; init; }

    /// <summary>Gets the default logging driver.</summary>
    public string LoggingDriver { get; init; }

    /// <summary>Gets the number of host CPUs the daemon sees.</summary>
    public long CpuCount { get; init; }

    /// <summary>Gets the total host memory in bytes.</summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>Gets the number of containers the daemon knows about.</summary>
    public long ContainerCount { get; init; }

    /// <summary>Gets the number of running containers.</summary>
    public long ContainersRunning { get; init; }

    /// <summary>Gets the number of paused containers.</summary>
    public long ContainersPaused { get; init; }

    /// <summary>Gets the number of stopped containers.</summary>
    public long ContainersStopped { get; init; }

    /// <summary>Gets the number of local images.</summary>
    public long ImageCount { get; init; }

    /// <summary>Gets a value indicating whether memory limits are supported.</summary>
    public bool HasMemoryLimitSupport { get; init; }

    /// <summary>Gets a value indicating whether swap limits are supported.</summary>
    public bool HasSwapLimitSupport { get; init; }

    /// <summary>Gets the warnings the daemon reported; never null.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
