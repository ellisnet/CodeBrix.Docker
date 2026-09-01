using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>Everything the picker and the create form need to know about one topology.</summary>
public sealed class TopologyDescriptor
{
    /// <summary>Gets the topology.</summary>
    public TopologyId Id { get; init; }

    /// <summary>Gets the two-character code, for example <c>D2</c>.</summary>
    public string Code { get; init; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; init; }

    /// <summary>Gets the group the picker files this under.</summary>
    public TopologyCategory Category { get; init; }

    /// <summary>Gets one sentence for the picker row.</summary>
    public string Summary { get; init; }

    /// <summary>Gets two to four sentences for the detail pane.</summary>
    public string Detail { get; init; }

    /// <summary>Gets the image the nodes run.</summary>
    public string Image { get; init; }

    /// <summary>Gets how many containers the instance has.</summary>
    public int ContainerCount { get; init; }

    /// <summary>Gets how many host data ports the instance publishes.</summary>
    public int DataPortCount { get; init; }

    /// <summary>Gets how many host sentinel ports the instance publishes.</summary>
    public int SentinelPortCount { get; init; }

    /// <summary>Gets a value indicating whether cluster-bus ports are published as well.</summary>
    public bool NeedsBusPorts { get; init; }

    /// <summary>Gets how a client connects.</summary>
    public ConnectionShape ConnectionShape { get; init; }

    /// <summary>Gets the parameters the create form generates; never null.</summary>
    public IReadOnlyList<TopologyParameter> Parameters { get; init; } = [];

    /// <summary>Gets the chips shown on the picker row; never null.</summary>
    public IReadOnlyList<string> Highlights { get; init; } = [];
}
