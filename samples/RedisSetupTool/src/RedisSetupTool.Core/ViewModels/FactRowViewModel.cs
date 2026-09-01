// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One label/value row of a facts panel. Every detail pane in the app is built from these, so
/// a new field is one line of view-model code rather than a new piece of markup.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class FactRowViewModel
{
    /// <summary>Creates a fact row.</summary>
    /// <param name="label">The label on the left.</param>
    /// <param name="value">The value on the right.</param>
    /// <param name="isMonospace">Whether the value is an id, a port or a path.</param>
    public FactRowViewModel(string label, string value, bool isMonospace = false)
    {
        Label = label;
        Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
        IsMonospace = isMonospace;
    }

    /// <summary>The label on the left.</summary>
    public string Label { get; }

    /// <summary>The value on the right, never empty.</summary>
    public string Value { get; }

    /// <summary>Whether the value should render in Roboto Mono.</summary>
    public bool IsMonospace { get; }
}
