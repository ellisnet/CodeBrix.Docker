using System;

namespace RedisSetupTool.Bridges;

/// <summary>
/// The clipboard bridge every CodeBrix.Platform application uses: the page fills the delegate
/// in from its <c>DataContextChanged</c> handler, because only the page can reach
/// <c>Windows.ApplicationModel.DataTransfer.Clipboard</c>. A view model must behave sensibly
/// when the delegate is still null — that is how heads without a clipboard are supported.
/// </summary>
public interface ICopyToClipboard
{
    /// <summary>Puts the given text on the system clipboard. Null until the page wires it.</summary>
    Action<string> CopyTextToClipboard { get; set; }
}
