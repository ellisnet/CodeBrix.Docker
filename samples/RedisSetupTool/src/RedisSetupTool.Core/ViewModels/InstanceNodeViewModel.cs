using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.Services;
using System;
using System.Globalization;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One node of an instance card: a coloured dot, the node's role, its host port, and the two
/// ways in — the container detail pane and a console.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class InstanceNodeViewModel : SimpleViewModel
{
    private readonly Action<string, string> _openConsole;
    private readonly Action<string> _showContainer;

    /// <summary>Wraps one node of a discovered instance.</summary>
    /// <param name="node">The node to show.</param>
    /// <param name="showContainer">Shows the node's container in the Containers section.</param>
    /// <param name="openConsole">Opens a console inside the node's container.</param>
    public InstanceNodeViewModel(TopologyNode node, Action<string> showContainer,
        Action<string, string> openConsole)
    {
        ContainerId = node.ContainerId;
        ContainerName = node.ContainerName;
        IsRunning = node.IsRunning;
        HostPort = node.HostPort;
        Role = node.Role;
        RoleText = DescribeRole(node);
        PortText = node.BusHostPort.HasValue
            ? node.HostPort.ToString(CultureInfo.InvariantCulture) + " · bus "
                + node.BusHostPort.Value.ToString(CultureInfo.InvariantCulture)
            : node.HostPort.ToString(CultureInfo.InvariantCulture);
        StateText = Formatting.OrDash(node.State);
        _showContainer = showContainer;
        _openConsole = openConsole;
    }

    #region | Bindable properties |

    /// <summary>The node's container id.</summary>
    public string ContainerId { get; }

    /// <summary>The node's container name.</summary>
    public string ContainerName { get; }

    /// <summary>The role the node plays in its topology.</summary>
    public NodeRole Role { get; }

    /// <summary>The short role caption on the card, for example <c>replica 1</c>.</summary>
    public string RoleText { get; }

    /// <summary>The published host port, plus the cluster bus port when there is one.</summary>
    public string PortText { get; }

    /// <summary>The published host port on its own.</summary>
    public int HostPort { get; }

    /// <summary>Whether the node's container is running.</summary>
    public bool IsRunning { get; }

    /// <summary>The container's raw state word.</summary>
    public string StateText { get; }

    /// <summary>Green while the node runs, grey when it does not.</summary>
    public Brush DotBrush => IsRunning ? Palette.Good : Palette.Idle;

    #endregion

    #region | Commands and their implementations |

    /// <summary>Shows the node's container in the Containers section.</summary>
    public SimpleCommand OpenCommand => field ??=
        new SimpleCommand(() => _showContainer?.Invoke(ContainerId));

    /// <summary>Opens a console inside the node's container.</summary>
    public SimpleCommand ConsoleCommand => field ??= new SimpleCommand(
        () => IsRunning,
        () => _openConsole?.Invoke(ContainerId, ContainerName));

    #endregion

    private static string DescribeRole(TopologyNode node) => node.Role switch
    {
        NodeRole.Primary => "primary",
        NodeRole.Replica => "replica " + node.NodeIndex.ToString(CultureInfo.InvariantCulture),
        NodeRole.Sentinel => "sentinel " + node.NodeIndex.ToString(CultureInfo.InvariantCulture),
        NodeRole.ClusterPrimary => "shard primary",
        NodeRole.ClusterReplica => "shard replica",
        NodeRole.QuorumMaster => "master " + node.NodeIndex.ToString(CultureInfo.InvariantCulture),
        _ => node.Role.ToString().ToLowerInvariant(),
    };
}
