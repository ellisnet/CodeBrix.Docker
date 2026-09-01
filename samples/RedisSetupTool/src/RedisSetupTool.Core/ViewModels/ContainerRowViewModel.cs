using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One row of the container list: a state dot, the name, the image, the published ports, and —
/// when the container carries this tool's labels — the topology chip that says which instance it
/// belongs to.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ContainerRowViewModel : SimpleViewModel
{
    private readonly Action<ContainerRowViewModel> _select;
    private bool _isSelected;

    /// <summary>Wraps one container.</summary>
    /// <param name="container">The container to show.</param>
    /// <param name="select">What selecting the row does.</param>
    public ContainerRowViewModel(ContainerInfo container, Action<ContainerRowViewModel> select)
    {
        Info = container;
        _select = select;

        Id = container.Id;
        ShortId = container.ShortId;
        Name = Formatting.OrDash(container.Name);
        Image = Formatting.OrDash(container.Image);
        StatusText = Formatting.OrDash(container.Status);
        IsRunning = container.IsRunning;
        IsManaged = container.IsManaged;
        TopologyCode = container.TopologyCode ?? string.Empty;
        PortsText = DescribePorts(container.Ports);
    }

    #region | Bindable properties |

    /// <summary>The container this row shows.</summary>
    public ContainerInfo Info { get; }

    /// <summary>The container's full id.</summary>
    public string Id { get; }

    /// <summary>The container's twelve-character id.</summary>
    public string ShortId { get; }

    /// <summary>The container's display name.</summary>
    public string Name { get; }

    /// <summary>The image the container runs.</summary>
    public string Image { get; }

    /// <summary>The daemon's own status line, for example <c>Up 3 minutes</c>.</summary>
    public string StatusText { get; }

    /// <summary>Whether the container is running.</summary>
    public bool IsRunning { get; }

    /// <summary>Whether this tool created the container.</summary>
    public bool IsManaged { get; }

    /// <summary>The topology chip's caption, empty when the container is not ours.</summary>
    public string TopologyCode { get; }

    /// <summary>Whether the topology chip is showing.</summary>
    public Visibility TopologyVisibility => GetVisibility(!string.IsNullOrEmpty(TopologyCode));

    /// <summary>The published ports, as one monospace line.</summary>
    public string PortsText { get; }

    /// <summary>Whether the ports line is showing.</summary>
    public Visibility PortsVisibility => GetVisibility(!string.IsNullOrEmpty(PortsText));

    /// <summary>Green while running, grey when stopped.</summary>
    public Brush DotBrush => IsRunning ? Palette.Good : Palette.Idle;

    /// <summary>Whether this is the selected row.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) { return; }
            _isSelected = value;
            NotifyPropertyChanged(nameof(IsSelected));
            NotifyPropertyChanged(nameof(RowBackground));
        }
    }

    /// <summary>The row's background: raised while selected.</summary>
    public Brush RowBackground => _isSelected ? Palette.Raised : Palette.Transparent;

    #endregion

    #region | Commands and their implementations |

    /// <summary>Selects this row.</summary>
    public SimpleCommand SelectCommand => field ??= new SimpleCommand(() => _select?.Invoke(this));

    #endregion

    private static string DescribePorts(IReadOnlyList<PortMapping> ports)
    {
        if (ports is null || ports.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var port in ports)
        {
            if (!string.IsNullOrEmpty(port.Display))
            {
                parts.Add(port.Display);
            }
        }
        return string.Join("  ", parts);
    }
}
