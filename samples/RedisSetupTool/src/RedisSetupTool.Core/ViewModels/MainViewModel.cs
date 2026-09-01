using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.Bridges;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.ViewModels;

/// <summary>
/// The shell: the header, the navigation rail, and the eight sections. The rail switches
/// sections by flipping <see cref="Microsoft.UI.Xaml.Visibility"/> properties over eight sibling
/// grids, which keeps every section's state — an open console, a running stats stream, a
/// half-filled create form — alive while another one is on screen.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, ICopyToClipboard, IShellContext,
    IInstanceParameterSink
{
    private readonly AppState _state;
    private readonly IDockerManager _docker;
    private readonly RefreshCoordinator _refresh;
    private readonly StartupAutomation _automation;
    private CancellationTokenSource _eventStream;
    private SectionKey _currentSection = SectionKey.Dashboard;
    private bool _isRefreshing;

    /// <summary>Creates the shell and starts its first refresh.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        _state = GetService<AppState>();
        _docker = GetService<IDockerManager>();
        _automation = new StartupAutomation(this);

        Dashboard = new DashboardViewModel(this);
        Instances = new InstancesViewModel(this);
        CreateInstance = new CreateInstanceViewModel(this);
        Containers = new ContainersViewModel(this);
        Consoles = new ConsolesViewModel(this);
        Images = new ImagesViewModel(this);
        NetworksVolumes = new NetworksVolumesViewModel(this);
        System = new SystemViewModel(this);

        BuildNavigation();

        _refresh = new RefreshCoordinator();
        _refresh.Tick += () => _ = RefreshAsync();
        _refresh.Start();

        _ = InitializeAsync();
    }

    #region | Bindable properties |

    /// <summary>The application's name, in the header.</summary>
    public string AppTitle => "Redis Setup Tool";

    /// <summary>The one-line strapline under the title.</summary>
    public string AppSubtitle =>
        "Docker containers and Redis topologies, on your own machine";

    /// <summary>Whether the last refresh reached the daemon.</summary>
    [AffectsAllCommands]
    public bool IsDaemonReachable
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The header pill's text, for example <c>Docker 29.7.2 · API 1.55</c>.</summary>
    public string DaemonPillText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "connecting…";

    /// <summary>The header pill's dot colour: green when the daemon answers, red when it does not.</summary>
    public Brush DaemonPillBrush
    {
        get;
        private set => SetProperty(ref field, value);
    } = Palette.Warn;

    /// <summary>The header's right-hand caption: when the snapshot was last taken.</summary>
    public string LastRefreshedText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The rail's rows.</summary>
    public ObservableCollection<NavItemViewModel> NavigationItems { get; } = [];

    /// <summary>The rail footer's first line.</summary>
    public string FooterInstanceText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The rail footer's second line.</summary>
    public string FooterContainerText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>Whether a refresh is in flight.</summary>
    [AffectsAllCommands]
    public bool IsBusy
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The dashboard.</summary>
    public DashboardViewModel Dashboard { get; }

    /// <summary>The Redis instances section.</summary>
    public InstancesViewModel Instances { get; }

    /// <summary>The create-instance form.</summary>
    public CreateInstanceViewModel CreateInstance { get; }

    /// <summary>The containers section.</summary>
    public ContainersViewModel Containers { get; }

    /// <summary>The consoles section.</summary>
    public ConsolesViewModel Consoles { get; }

    /// <summary>The images section.</summary>
    public ImagesViewModel Images { get; }

    /// <summary>The networks-and-volumes section.</summary>
    public NetworksVolumesViewModel NetworksVolumes { get; }

    /// <summary>The system section.</summary>
    public SystemViewModel System { get; }

    /// <summary>Which section is on screen.</summary>
    public SectionKey CurrentSection => _currentSection;

    /// <summary>Whether the Dashboard section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility DashboardVisibility =>
        GetVisibility(_currentSection == SectionKey.Dashboard);

    /// <summary>Whether the Redis Instances section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility InstancesVisibility =>
        GetVisibility(_currentSection == SectionKey.Instances);

    /// <summary>Whether the create-instance form is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility CreateInstanceVisibility =>
        GetVisibility(_currentSection == SectionKey.CreateInstance);

    /// <summary>Whether the Containers section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility ContainersVisibility =>
        GetVisibility(_currentSection == SectionKey.Containers);

    /// <summary>Whether the Consoles section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility ConsolesVisibility =>
        GetVisibility(_currentSection == SectionKey.Consoles);

    /// <summary>Whether the Images section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility ImagesVisibility =>
        GetVisibility(_currentSection == SectionKey.Images);

    /// <summary>Whether the Networks and Volumes section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility NetworksVolumesVisibility =>
        GetVisibility(_currentSection == SectionKey.NetworksVolumes);

    /// <summary>Whether the System section is on screen.</summary>
    public global::Microsoft.UI.Xaml.Visibility SystemVisibility =>
        GetVisibility(_currentSection == SectionKey.System);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??= new SimpleCommand((Func<Task>)RefreshAsync);

    /// <summary>Shows the create-instance form.</summary>
    public SimpleCommand NewInstanceCommand => field ??=
        new SimpleCommand(() => Navigate(SectionKey.CreateInstance));

    #endregion

    #region | ICopyToClipboard implementation |

    /// <inheritdoc />
    public Action<string> CopyTextToClipboard { get; set; }

    #endregion

    #region | IShellContext implementation |

    /// <inheritdoc />
    public AppState State => _state;

    /// <inheritdoc />
    public void Navigate(SectionKey section)
    {
        if (_currentSection == section) { return; }

        //Leaving a section cancels whatever live feed it was running: the container stats
        //  stream and the log poll are the two that would otherwise keep asking the daemon.
        if (_currentSection == SectionKey.Containers) { Containers.Suspend(); }

        _currentSection = section;
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Section == section;
        }

        NotifyPropertyChanged(nameof(CurrentSection));
        NotifyPropertyChanged(nameof(DashboardVisibility));
        NotifyPropertyChanged(nameof(InstancesVisibility));
        NotifyPropertyChanged(nameof(CreateInstanceVisibility));
        NotifyPropertyChanged(nameof(ContainersVisibility));
        NotifyPropertyChanged(nameof(ConsolesVisibility));
        NotifyPropertyChanged(nameof(ImagesVisibility));
        NotifyPropertyChanged(nameof(NetworksVolumesVisibility));
        NotifyPropertyChanged(nameof(SystemVisibility));

        SectionChanged?.Invoke(section);
        LogAutomation("navigate " + section.ToString());
    }

    /// <inheritdoc />
    public void CopyToClipboard(string text)
    {
        //A head with no clipboard leaves the delegate null; copying is then simply a no-op.
        CopyTextToClipboard?.Invoke(text);
    }

    /// <inheritdoc />
    public async Task RefreshAsync()
    {
        if (_isRefreshing) { return; }

        _isRefreshing = true;
        IsBusy = true;
        try
        {
            await _state.RefreshAsync().ConfigureAwait(true);
            ApplyState();
        }
        finally
        {
            IsBusy = false;
            _isRefreshing = false;
        }
    }

    /// <inheritdoc />
    public void OpenConsole(string containerId, string containerName)
    {
        Navigate(SectionKey.Consoles);
        Consoles.OpenConsole(containerId, containerName);
    }

    /// <inheritdoc />
    public void ShowContainer(string containerId)
    {
        Navigate(SectionKey.Containers);
        Containers.SelectById(containerId);
    }

    /// <inheritdoc />
    public void PauseAutoRefresh() => _refresh?.Pause();

    /// <inheritdoc />
    public void ResumeAutoRefresh() => _refresh?.Resume();

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string message, string title) => ConfirmDialog(message, title);

    /// <inheritdoc />
    public Task ShowErrorAsync(string message, string details = null) =>
        ShowError(message, details);

    /// <inheritdoc />
    public Task ShowInfoAsync(string message) => ShowInfo(message);

    /// <inheritdoc />
    public void LogAutomation(string line) => _automation?.Log(line);

    #endregion

    /// <summary>
    /// Destroys an instance without asking for confirmation, for the unattended verification
    /// script. It takes the same path an instance card's Destroy button takes, minus the dialog
    /// that an unattended run has nobody to answer.
    /// </summary>
    /// <param name="instanceId">The instance to destroy.</param>
    /// <returns>A task that completes once the instance is gone and the app has refreshed.</returns>
    public async Task DestroyForAutomationAsync(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) { return; }

        PauseAutoRefresh();
        try
        {
            await GetService<global::RedisSetupTool.DockerManagement.Topologies
                .IRedisTopologyService>().DestroyAsync(instanceId).ConfigureAwait(true);
        }
        finally
        {
            ResumeAutoRefresh();
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    #region | IInstanceParameterSink implementation |

    /// <inheritdoc />
    public void RememberInstanceParameters(string instanceId,
        IReadOnlyDictionary<string, string> parameters) =>
        Instances.RememberParameters(instanceId, parameters);

    #endregion

    /// <summary>
    /// Raised whenever the rail switches sections. The page does not need it in the shipped
    /// shape — the eight visibility properties do the work — but it keeps the alternative
    /// nested-frame shape one line away.
    /// </summary>
    public event Action<SectionKey> SectionChanged;

    private void BuildNavigation()
    {
        NavigationItems.Add(new NavItemViewModel(SectionKey.Dashboard, "Dashboard", "",
            Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.Instances, "Redis instances", "",
            Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.CreateInstance, "New instance",
            "", Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.Containers, "Containers", "",
            Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.Consoles, "Consoles", "",
            Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.Images, "Images", "", Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.NetworksVolumes, "Networks & volumes",
            "", Navigate));
        NavigationItems.Add(new NavItemViewModel(SectionKey.System, "System", "", Navigate));

        NavigationItems[0].IsSelected = true;
    }

    private async Task InitializeAsync()
    {
        await RefreshAsync().ConfigureAwait(true);
        StartEventStream();
        await _automation.RunAsync().ConfigureAwait(true);
    }

    private void ApplyState()
    {
        IsDaemonReachable = _state.IsDaemonReachable;
        var daemon = _state.Daemon;
        DaemonPillText = _state.IsDaemonReachable && daemon is not null
            ? "Docker " + daemon.ServerVersion + " · API " + daemon.ApiVersion
            : "daemon unreachable";
        DaemonPillBrush = _state.IsDaemonReachable ? Palette.Good : Palette.Bad;
        LastRefreshedText = _state.IsDaemonReachable
            ? "updated " + Formatting.Clock(_state.LastRefreshed)
            : Formatting.OrDash(_state.LastError);

        FooterInstanceText = Formatting.Plural(_state.Instances.Count, "instance");
        FooterContainerText = _state.RunningContainerCount.ToString() + " of "
            + Formatting.Plural(_state.Containers.Count, "container") + " running";

        SetBadge(SectionKey.Instances, _state.Instances.Count);
        SetBadge(SectionKey.Containers, _state.Containers.Count);
        SetBadge(SectionKey.Images, _state.Images.Count);
        SetBadge(SectionKey.Consoles, Consoles.Tabs.Count);

        Dashboard.ApplySnapshot();
        Instances.ApplySnapshot();
        CreateInstance.ApplySnapshot();
        Containers.ApplySnapshot();
        Consoles.ApplySnapshot();
        Images.ApplySnapshot();
        NetworksVolumes.ApplySnapshot();
        System.ApplySnapshot();
    }

    private void SetBadge(SectionKey section, int count)
    {
        foreach (var item in NavigationItems)
        {
            if (item.Section == section)
            {
                item.BadgeText = count > 0 ? count.ToString() : string.Empty;
                return;
            }
        }
    }

    private void StartEventStream()
    {
        _eventStream?.Cancel();
        _eventStream = new CancellationTokenSource();
        _ = PumpEventsAsync(_eventStream.Token);
    }

    private async Task PumpEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var daemonEvent in _docker.StreamEventsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) { return; }

                //One stream feeds both the dashboard's recent-activity strip and the System
                //  section's full log, so the daemon is only asked once.
                InvokeOnMainThread(() =>
                {
                    Dashboard.AddEvent(daemonEvent);
                    System.AddEvent(daemonEvent);
                });
            }
        }
        catch (OperationCanceledException)
        {
            //The stream ends when the app closes.
        }
        catch (Exception)
        {
            //An event stream that drops out is not worth interrupting the user for; the
            //  periodic refresh keeps the rest of the app current either way.
        }
    }
}
