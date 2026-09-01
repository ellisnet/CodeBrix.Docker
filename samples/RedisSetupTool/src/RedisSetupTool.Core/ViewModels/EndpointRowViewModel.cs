using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One copyable row of an instance card's CONNECT block: a label, the value in Roboto Mono, and
/// a copy button that briefly says "Copied". A password row additionally hides its value behind
/// bullets until the reveal button is pressed.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class EndpointRowViewModel : SimpleViewModel
{
    private readonly Action<string> _copy;
    private readonly string _secret;
    private bool _isRevealed;
    private bool _isCopied;

    /// <summary>Creates a plain copyable row.</summary>
    /// <param name="label">The label on the left.</param>
    /// <param name="value">The value to show and copy.</param>
    /// <param name="copy">What copying does.</param>
    public EndpointRowViewModel(string label, string value, Action<string> copy)
    {
        Label = label;
        Value = value ?? string.Empty;
        _copy = copy;
    }

    private EndpointRowViewModel(string label, string secret, Action<string> copy, bool isSecret)
    {
        Label = label;
        _secret = secret ?? string.Empty;
        _copy = copy;
        IsSecret = isSecret;
        Value = new string('•', Math.Min(12, Math.Max(6, _secret.Length)));
    }

    /// <summary>
    /// Creates a masked row whose value is hidden behind bullets until it is revealed. The
    /// password is also readable through <c>docker inspect</c>, so this is convenience rather
    /// than secrecy — the card says so.
    /// </summary>
    /// <param name="label">The label on the left.</param>
    /// <param name="secret">The value to hide.</param>
    /// <param name="copy">What copying does.</param>
    /// <returns>The masked row.</returns>
    public static EndpointRowViewModel Secret(string label, string secret, Action<string> copy) =>
        new(label, secret, copy, true);

    #region | Bindable properties |

    /// <summary>The label on the left.</summary>
    public string Label { get; }

    /// <summary>The value shown on the row: bullets while a secret is hidden.</summary>
    public string Value { get; private set; }

    /// <summary>Whether this row hides its value.</summary>
    public bool IsSecret { get; }

    /// <summary>Whether the reveal button is offered.</summary>
    public Visibility RevealVisibility => GetVisibility(IsSecret);

    /// <summary>The reveal button's caption.</summary>
    public string RevealText => _isRevealed ? "hide" : "show";

    /// <summary>Whether the transient "Copied" confirmation is showing.</summary>
    public Visibility CopiedVisibility => GetVisibility(_isCopied);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Puts the row's real value on the clipboard.</summary>
    public SimpleCommand CopyCommand => field ??= new SimpleCommand((Func<Task>)CopyAsync);

    /// <summary>Shows or hides a masked value.</summary>
    public SimpleCommand ToggleRevealCommand => field ??= new SimpleCommand(ToggleReveal);

    private async Task CopyAsync()
    {
        var text = IsSecret ? _secret : Value;
        if (string.IsNullOrEmpty(text)) { return; }

        _copy?.Invoke(text);
        _isCopied = true;
        NotifyPropertyChanged(nameof(CopiedVisibility));

        //The confirmation is a nicety, not state: it fades on its own after a moment.
        await Task.Delay(1500).ConfigureAwait(true);
        _isCopied = false;
        NotifyPropertyChanged(nameof(CopiedVisibility));
    }

    private void ToggleReveal()
    {
        if (!IsSecret) { return; }

        _isRevealed = !_isRevealed;
        Value = _isRevealed
            ? _secret
            : new string('•', Math.Min(12, Math.Max(6, _secret.Length)));
        NotifyPropertyChanged(nameof(Value));
        NotifyPropertyChanged(nameof(RevealText));
    }

    #endregion
}
