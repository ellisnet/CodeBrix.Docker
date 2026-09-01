using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.Services;
using System;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One row of the navigation rail: a glyph, a title, an optional count badge and the brushes
/// that show whether it is the selected row. The family does selection highlighting with
/// <see cref="Brush"/>-typed view-model properties rather than styles or triggers, so that is
/// what this exposes.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class NavItemViewModel : SimpleViewModel
{
    private readonly Action<SectionKey> _select;
    private bool _isSelected;
    private string _badgeText = string.Empty;

    /// <summary>Creates a rail item.</summary>
    /// <param name="section">The section this item shows.</param>
    /// <param name="title">The label beside the glyph.</param>
    /// <param name="glyph">The Fluent symbol glyph, as a single-character string.</param>
    /// <param name="select">What selecting the item does.</param>
    public NavItemViewModel(SectionKey section, string title, string glyph,
        Action<SectionKey> select)
    {
        Section = section;
        Title = title;
        Glyph = glyph;
        _select = select ?? throw new ArgumentNullException(nameof(select));
    }

    #region | Bindable properties |

    /// <summary>The section this item shows.</summary>
    public SectionKey Section { get; }

    /// <summary>The label beside the glyph.</summary>
    public string Title { get; }

    /// <summary>The Fluent symbol glyph.</summary>
    public string Glyph { get; }

    /// <summary>Whether this is the section currently on screen.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) { return; }
            _isSelected = value;
            NotifyPropertyChanged(nameof(IsSelected));
            NotifyPropertyChanged(nameof(ItemBackground));
            NotifyPropertyChanged(nameof(ItemForeground));
        }
    }

    /// <summary>The row's background: raised while selected, invisible otherwise.</summary>
    public Brush ItemBackground => _isSelected ? Palette.Raised : Palette.Transparent;

    /// <summary>The row's text and glyph colour: accent while selected.</summary>
    public Brush ItemForeground => _isSelected ? Palette.Accent : Palette.TextSecondary;

    /// <summary>The count badge's text; an empty string hides the badge.</summary>
    public string BadgeText
    {
        get => _badgeText;
        set
        {
            SetProperty(ref _badgeText, value ?? string.Empty);
            NotifyPropertyChanged(nameof(BadgeVisibility));
        }
    }

    /// <summary>Whether the count badge is showing.</summary>
    public Visibility BadgeVisibility => GetVisibility(!string.IsNullOrEmpty(_badgeText));

    #endregion

    #region | Commands and their implementations |

    /// <summary>Shows this item's section.</summary>
    public SimpleCommand SelectCommand => field ??= new SimpleCommand(() => _select(Section));

    #endregion
}
