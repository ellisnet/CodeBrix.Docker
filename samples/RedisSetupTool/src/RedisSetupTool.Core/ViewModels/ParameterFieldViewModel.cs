using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement.Topologies;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One generated field of the create form. The family has no <c>DataTemplateSelector</c>, so a
/// single template carries all six editors and each one has its own <see cref="Visibility"/>
/// property here; exactly one is ever visible.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ParameterFieldViewModel : SimpleViewModel
{
    private readonly Action _changed;

    //Explicit backing fields: the constructor seeds all three without raising the change
    //  callback, which would revalidate a form that is still being built.
    private string _value = string.Empty;
    private double _numberValue;
    private bool _boolValue;

    /// <summary>Builds a field from a topology parameter.</summary>
    /// <param name="parameter">The parameter to edit.</param>
    /// <param name="changed">Called whenever the value changes, so the form can revalidate.</param>
    public ParameterFieldViewModel(TopologyParameter parameter, Action changed)
    {
        Key = parameter.Key;
        Label = parameter.Label;
        HelpText = parameter.HelpText ?? string.Empty;
        Kind = parameter.Kind;
        IsRequired = parameter.IsRequired;
        Minimum = parameter.Minimum ?? 0L;
        Maximum = parameter.Maximum ?? long.MaxValue;
        _changed = changed;

        foreach (var choice in parameter.Choices)
        {
            Choices.Add(choice);
        }

        //A password field opens on a freshly generated value rather than the literal token,
        //  so the field shows what will actually be used.
        var initial = parameter.DefaultValue ?? string.Empty;
        if (Kind == TopologyParameterKind.Password
            && string.Equals(initial, TopologyParameter.GeneratedToken, StringComparison.Ordinal))
        {
            initial = GeneratePassword();
        }

        _value = initial;
        _numberValue = Kind == TopologyParameterKind.Integer
            && long.TryParse(initial, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed)
            ? parsed
            : Minimum;
        _boolValue = string.Equals(initial, "true", StringComparison.OrdinalIgnoreCase);
    }

    #region | Bindable properties |

    /// <summary>The parameter key the create request carries.</summary>
    public string Key { get; }

    /// <summary>The field's caption.</summary>
    public string Label { get; }

    /// <summary>The sentence under the editor.</summary>
    public string HelpText { get; }

    /// <summary>Whether the field must be filled in.</summary>
    public bool IsRequired { get; }

    /// <summary>Which editor this field uses.</summary>
    public TopologyParameterKind Kind { get; }

    /// <summary>The choices offered by a <see cref="TopologyParameterKind.Choice"/> field.</summary>
    public ObservableCollection<string> Choices { get; } = [];

    /// <summary>The smallest value an integer field accepts.</summary>
    public double Minimum { get; }

    /// <summary>The largest value an integer field accepts.</summary>
    public double Maximum { get; }

    /// <summary>
    /// The field's value as the create request will carry it. The integer and boolean editors
    /// write through their own typed properties, which keep this in step.
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            SetProperty(ref _value, value ?? string.Empty);
            _changed?.Invoke();
        }
    }

    /// <summary>
    /// The value of an integer field. <c>NumberBox</c> reports an emptied box as
    /// <see cref="double.NaN"/>, which is left in place rather than written through, so the
    /// request keeps the last good number and validation still sees it.
    /// </summary>
    public double NumberValue
    {
        get => _numberValue;
        set
        {
            //There is no double overload of SetProperty, so compare and notify by hand.
            if (double.IsNaN(value)) { return; }
            if (Math.Abs(_numberValue - value) < 0.0001d) { return; }
            _numberValue = value;
            NotifyPropertyChanged(nameof(NumberValue));
            Value = ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The value of a boolean field.</summary>
    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            SetProperty(ref _boolValue, value);
            Value = value ? "true" : "false";
        }
    }

    /// <summary>Whether the plain-text editor is showing.</summary>
    public Visibility TextVisibility => GetVisibility(Kind == TopologyParameterKind.Text);

    /// <summary>Whether the password editor is showing.</summary>
    public Visibility PasswordVisibility => GetVisibility(Kind == TopologyParameterKind.Password);

    /// <summary>Whether the number editor is showing.</summary>
    public Visibility IntegerVisibility => GetVisibility(Kind == TopologyParameterKind.Integer);

    /// <summary>Whether the choice editor is showing.</summary>
    public Visibility ChoiceVisibility => GetVisibility(Kind == TopologyParameterKind.Choice);

    /// <summary>Whether the switch is showing.</summary>
    public Visibility BooleanVisibility => GetVisibility(Kind == TopologyParameterKind.Boolean);

    /// <summary>Whether the multi-line editor is showing.</summary>
    public Visibility MultiLineVisibility =>
        GetVisibility(Kind == TopologyParameterKind.MultiLineText);

    /// <summary>Whether the "generate" button is offered.</summary>
    public Visibility GenerateVisibility =>
        GetVisibility(Kind == TopologyParameterKind.Password
            || (Kind == TopologyParameterKind.MultiLineText && Key == "users"));

    /// <summary>Whether the help sentence is showing.</summary>
    public Visibility HelpVisibility => GetVisibility(!string.IsNullOrEmpty(HelpText));

    #endregion

    #region | Commands and their implementations |

    /// <summary>Puts a fresh random password in the field.</summary>
    public SimpleCommand GenerateCommand => field ??= new SimpleCommand(Generate);

    private void Generate()
    {
        if (Kind == TopologyParameterKind.Password)
        {
            Value = GeneratePassword();
            return;
        }

        //The ACL user list: re-generate every {generated} placeholder it still carries, and
        //  replace the password of any line that has already been filled in.
        var lines = Value.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var parts = lines[index].TrimEnd('\r').Split(':');
            if (parts.Length >= 2)
            {
                parts[1] = GeneratePassword();
                lines[index] = string.Join(":", parts);
            }
        }
        Value = string.Join("\n", lines);
    }

    #endregion

    private static string GeneratePassword() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(9));
}
