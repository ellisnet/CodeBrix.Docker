using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.Services;
using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// What the eight sections have in common: the shell they can ask for navigation and the
/// clipboard, the shared daemon snapshot, a busy flag, and one place that turns an exception
/// from the daemon into a readable line rather than a crash.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public abstract class SectionViewModel : SimpleViewModel
{
    /// <summary>Creates a section over the shell that owns it.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    protected SectionViewModel(IShellContext shell)
    {
        Shell = shell;
    }

    /// <summary>The shell that owns this section. Null in design mode.</summary>
    protected IShellContext Shell { get; }

    /// <summary>The shared daemon snapshot.</summary>
    protected AppState State => Shell?.State;

    #region | Bindable properties |

    /// <summary>Whether this section is waiting on the daemon.</summary>
    [AffectsAllCommands]
    public bool IsBusy
    {
        get;
        protected set => SetProperty(ref field, value);
    }

    /// <summary>The inverse of <see cref="IsBusy"/>, for controls that gate on it.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>The last thing that went wrong in this section, or an empty string.</summary>
    public string ErrorText
    {
        get;
        protected set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>Whether an error line is showing.</summary>
    public Visibility ErrorVisibility => GetVisibility(!string.IsNullOrEmpty(ErrorText));

    #endregion

    /// <summary>
    /// Re-reads whatever this section shows out of the shared snapshot. Always called on the
    /// UI thread, after a refresh has replaced the snapshot.
    /// </summary>
    public abstract void ApplySnapshot();

    /// <summary>
    /// Runs a daemon operation with the busy flag set, turning any failure into
    /// <see cref="ErrorText"/> rather than an unhandled exception.
    /// </summary>
    /// <param name="work">The operation to run.</param>
    /// <param name="refreshAfter">Whether to refresh the whole app afterwards.</param>
    /// <returns>True when the operation completed without error.</returns>
    protected async Task<bool> RunAsync(Func<Task> work, bool refreshAfter = true)
    {
        if (work is null) { return false; }

        IsBusy = true;
        NotifyPropertyChanged(nameof(IsNotBusy));
        SetError(null);
        try
        {
            await work().ConfigureAwait(true);
            if (refreshAfter && Shell is not null)
            {
                await Shell.RefreshAsync().ConfigureAwait(true);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            //A cancelled operation is the user changing their mind, not a failure.
            return false;
        }
        catch (DockerManagementException exception)
        {
            SetError(exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>Shows, or clears, the section's error line.</summary>
    /// <param name="message">The message, or null to clear it.</param>
    protected void SetError(string message)
    {
        ErrorText = message ?? string.Empty;
        NotifyPropertyChanged(nameof(ErrorVisibility));
    }
}
