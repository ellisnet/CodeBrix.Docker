using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;
using RedisSetupTool.Bridges;
using RedisSetupTool.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace RedisSetupTool.Views;

/// <summary>
/// The application's one page: a header, a navigation rail, and eight sibling section grids
/// switched by <c>Visibility</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shell shape (task B0 of the design): Shape V, sibling grids switched by Visibility.</b>
/// The prescribed spike was run on the Linux X11 head and a nested <c>Frame</c> works — three
/// navigations succeeded, each page constructed, loaded and sized correctly (816x640), and a
/// nested page's own <c>Page.DataContext</c> resolved. Shape V was chosen anyway, for two
/// reasons the spike also showed:
/// </para>
/// <list type="number">
/// <item><description>A <c>Frame</c> constructs a fresh page — and therefore a fresh view model
/// — on every navigation, including a return to a page already visited. This tool's sections
/// hold live state: an open console session, a running stats stream, a half-filled create form.
/// All of it would be discarded on every rail click.</description></item>
/// <item><description>Navigating away unloads the section's visual tree. <c>TerminalControl</c>
/// silently drops anything fed to it before <c>Loaded</c>, so a console tab would lose output
/// while the user looked at another section. A second spike proved the alternative works: a
/// <c>TerminalControl</c> created inside a <b>collapsed</b> grid raises <c>Loaded</c> only when
/// that grid becomes visible, and keeps its grid size and content afterwards.</description></item>
/// </list>
/// <para>
/// The section markup is self-contained either way, so the choice stays reversible.
/// </para>
/// </remarks>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        //Doing this before InitializeComponent() - in case InitializeComponent()
        //  is the thing that sets the data context.
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

            if (DataContext is ICopyToClipboard copy)
            {
                copy.CopyTextToClipboard = (text) =>
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        var clipData = new DataPackage();
                        clipData.SetText(text);
                        Clipboard.SetContent(clipData);
                    }
                };
            }

            //The console tabs are built in code-behind and mirror the view model's collection
            AttachConsoles(DataContext as MainViewModel);
        };

        this.InitializeComponent(); //Leave this line last
    }

    private MainViewModel ViewModel => DataContext as MainViewModel;
}
