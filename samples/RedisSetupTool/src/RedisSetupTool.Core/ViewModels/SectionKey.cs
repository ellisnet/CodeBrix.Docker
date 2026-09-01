// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>The eight sections the rail switches between.</summary>
public enum SectionKey
{
    /// <summary>What is running right now, in one screen.</summary>
    Dashboard,

    /// <summary>The Redis instances this tool created.</summary>
    Instances,

    /// <summary>The topology catalog and the create form.</summary>
    CreateInstance,

    /// <summary>Every container on the daemon, with its full lifecycle.</summary>
    Containers,

    /// <summary>Live terminal sessions inside running containers.</summary>
    Consoles,

    /// <summary>Every image on the daemon, with the analysis tools.</summary>
    Images,

    /// <summary>Networks and volumes.</summary>
    NetworksVolumes,

    /// <summary>Daemon information, disk usage, events and the advisor sweep.</summary>
    System,
}
