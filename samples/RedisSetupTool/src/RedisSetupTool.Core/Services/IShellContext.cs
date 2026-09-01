using RedisSetupTool.ViewModels;
using System.Threading.Tasks;

namespace RedisSetupTool.Services;

/// <summary>
/// What a section view model may ask of the shell. <c>MainViewModel</c> implements it and
/// hands itself to every child at construction, which keeps the children free of a back
/// reference to the whole shell and makes the small set of cross-section moves explicit.
/// </summary>
public interface IShellContext
{
    /// <summary>The shared daemon snapshot every section reads.</summary>
    AppState State { get; }

    /// <summary>Shows the given section.</summary>
    /// <param name="section">The section to show.</param>
    void Navigate(SectionKey section);

    /// <summary>Puts text on the clipboard, doing nothing on a head with no clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    void CopyToClipboard(string text);

    /// <summary>Re-reads everything from the daemon and pushes it into every section.</summary>
    /// <returns>A task that completes when the refresh has been applied.</returns>
    Task RefreshAsync();

    /// <summary>Opens a console tab on a container and shows the Consoles section.</summary>
    /// <param name="containerId">The container to open a shell in.</param>
    /// <param name="containerName">The name to put on the tab.</param>
    void OpenConsole(string containerId, string containerName);

    /// <summary>Shows the Containers section with the given container selected.</summary>
    /// <param name="containerId">The container to select.</param>
    void ShowContainer(string containerId);

    /// <summary>Suppresses the periodic refresh while a long operation runs.</summary>
    void PauseAutoRefresh();

    /// <summary>Lets the periodic refresh resume.</summary>
    void ResumeAutoRefresh();

    /// <summary>
    /// Asks the user to confirm something. Dialogs go through the shell because only the view
    /// model the page set as its DataContext has been given a <c>XamlRoot</c> to attach one to.
    /// </summary>
    /// <param name="message">What the user is agreeing to.</param>
    /// <param name="title">The dialog's title.</param>
    /// <returns>True when the user said yes.</returns>
    Task<bool> ConfirmAsync(string message, string title);

    /// <summary>Reports a failure to the user.</summary>
    /// <param name="message">The one-line summary.</param>
    /// <param name="details">The longer explanation, or null.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowErrorAsync(string message, string details = null);

    /// <summary>Tells the user something.</summary>
    /// <param name="message">The message to show.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowInfoAsync(string message);

    /// <summary>Writes one line to the app's automation log, when automation is running.</summary>
    /// <param name="line">The line to write.</param>
    void LogAutomation(string line);
}
