using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One row of the topology catalog: the code chip, the display name, how many containers it
/// takes, and the highlight chips. Selection highlighting is a pair of brush properties, which
/// is how the family does it.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class TopologyChoiceViewModel : SimpleViewModel
{
    private readonly Action<TopologyChoiceViewModel> _select;
    private bool _isSelected;

    /// <summary>Wraps one catalog entry.</summary>
    /// <param name="descriptor">The topology to offer.</param>
    /// <param name="select">What choosing the row does.</param>
    public TopologyChoiceViewModel(TopologyDescriptor descriptor,
        Action<TopologyChoiceViewModel> select)
    {
        Descriptor = descriptor;
        _select = select;
        CountText = descriptor.ContainerCount == 1
            ? "1 container"
            : descriptor.ContainerCount.ToString(CultureInfo.InvariantCulture) + " containers";
        foreach (var highlight in descriptor.Highlights)
        {
            Highlights.Add(highlight);
        }
    }

    #region | Bindable properties |

    /// <summary>The catalog entry this row offers.</summary>
    public TopologyDescriptor Descriptor { get; }

    /// <summary>The two-character code, for example <c>D2</c>.</summary>
    public string Code => Descriptor.Code;

    /// <summary>The topology's display name.</summary>
    public string DisplayName => Descriptor.DisplayName;

    /// <summary>The one-line summary.</summary>
    public string Summary => Descriptor.Summary;

    /// <summary>How many containers the topology takes.</summary>
    public string CountText { get; }

    /// <summary>The short highlight chips.</summary>
    public ObservableCollection<string> Highlights { get; } = [];

    /// <summary>Whether this is the chosen topology.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) { return; }
            _isSelected = value;
            NotifyPropertyChanged(nameof(IsSelected));
            NotifyPropertyChanged(nameof(RowBackground));
            NotifyPropertyChanged(nameof(RowBorder));
        }
    }

    /// <summary>The row's background: raised while chosen.</summary>
    public Brush RowBackground => _isSelected ? Palette.Raised : Palette.Card;

    /// <summary>The row's border: accent while chosen.</summary>
    public Brush RowBorder => _isSelected ? Palette.Accent : Palette.Hairline;

    #endregion

    #region | Commands and their implementations |

    /// <summary>Chooses this topology.</summary>
    public SimpleCommand SelectCommand => field ??= new SimpleCommand(() => _select?.Invoke(this));

    #endregion
}

/// <summary>One category of the topology catalog, with its rows under an all-caps header.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class TopologyGroupViewModel
{
    /// <summary>Creates a category group.</summary>
    /// <param name="category">The category this group holds.</param>
    public TopologyGroupViewModel(TopologyCategory category)
    {
        Category = category;
        Title = Describe(category);
    }

    /// <summary>The category this group holds.</summary>
    public TopologyCategory Category { get; }

    /// <summary>The all-caps header.</summary>
    public string Title { get; }

    /// <summary>The rows in this category.</summary>
    public ObservableCollection<TopologyChoiceViewModel> Items { get; } = [];

    private static string Describe(TopologyCategory category) => category switch
    {
        TopologyCategory.SingleNode => "SINGLE NODE",
        TopologyCategory.Replication => "REPLICATION",
        TopologyCategory.HighAvailability => "HIGH AVAILABILITY",
        TopologyCategory.Cluster => "CLUSTER",
        TopologyCategory.VersionMatrix => "VERSION MATRIX",
        TopologyCategory.Features => "FEATURES",
        TopologyCategory.Operational => "OPERATIONAL",
        TopologyCategory.Locking => "LOCKING",
        _ => category.ToString().ToUpperInvariant(),
    };
}
