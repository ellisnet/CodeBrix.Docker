using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>Which of the detail pane's five tabs is showing.</summary>
public enum ContainerTab
{
    /// <summary>Everything <c>docker inspect</c> knows, laid out as facts.</summary>
    Overview,

    /// <summary>The container's log text, polled rather than followed.</summary>
    Logs,

    /// <summary>Live CPU, memory, process, network and block figures.</summary>
    Stats,

    /// <summary>Throttling, memory breakdown, out-of-memory state and health.</summary>
    Diagnostics,

    /// <summary>What the advisor thinks of this container.</summary>
    Advisor,
}

/// <summary>
/// The right-hand pane of the Containers section: five tabs over one container, each fed by its
/// own daemon call, and a toolbar carrying the whole container lifecycle.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ContainerDetailViewModel : SimpleViewModel
{
    private readonly IShellContext _shell;
    private readonly IDockerManager _docker;
    private readonly Func<Task> _refreshAll;

    private CancellationTokenSource _statsCancellation;
    private CancellationTokenSource _logPolling;
    private string _containerId;
    private ContainerTab _tab = ContainerTab.Overview;

    /// <summary>Creates the detail pane.</summary>
    /// <param name="shell">The shell, for the clipboard, consoles and dialogs.</param>
    /// <param name="docker">The Docker facade.</param>
    /// <param name="refreshAll">Refreshes the whole app after a lifecycle operation.</param>
    public ContainerDetailViewModel(IShellContext shell, IDockerManager docker,
        Func<Task> refreshAll)
    {
        _shell = shell;
        _docker = docker;
        _refreshAll = refreshAll;

        LogTailOptions.Add("100 lines");
        LogTailOptions.Add("500 lines");
        LogTailOptions.Add("2000 lines");
        LogTailOptions.Add("Everything");
        KillSignals.Add("SIGKILL");
        KillSignals.Add("SIGTERM");
        KillSignals.Add("SIGINT");
        KillSignals.Add("SIGHUP");
    }

    #region | Bindable properties |

    /// <summary>Whether a container is selected at all.</summary>
    public Visibility HasSelectionVisibility => GetVisibility(!string.IsNullOrEmpty(_containerId));

    /// <summary>Whether nothing is selected, so the empty state shows.</summary>
    public Visibility NoSelectionVisibility => GetVisibility(string.IsNullOrEmpty(_containerId));

    /// <summary>The container's display name.</summary>
    public string Title
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The image and short id, under the title.</summary>
    public string Subtitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The state pill's caption.</summary>
    public string StateText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The state pill's colour.</summary>
    public Brush StateBrush
    {
        get;
        private set => SetProperty(ref field, value);
    } = Palette.Idle;

    /// <summary>Whether the selected container is running.</summary>
    [AffectsAllCommands]
    public bool IsRunning
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Whether a daemon call for this pane is in flight.</summary>
    [AffectsAllCommands]
    public bool IsBusy
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The last failure in this pane, or an empty string.</summary>
    public string ErrorText
    {
        get;
        private set
        {
            SetProperty(ref field, value ?? string.Empty);
            NotifyPropertyChanged(nameof(ErrorVisibility));
        }
    } = string.Empty;

    /// <summary>Whether the error line is showing.</summary>
    public Visibility ErrorVisibility => GetVisibility(!string.IsNullOrEmpty(ErrorText));

    /// <summary>The Overview tab's facts.</summary>
    public ObservableCollection<FactRowViewModel> OverviewFacts { get; } = [];

    /// <summary>The container's network attachments, one fact row each.</summary>
    public ObservableCollection<FactRowViewModel> NetworkFacts { get; } = [];

    /// <summary>The container's mounts, one fact row each.</summary>
    public ObservableCollection<FactRowViewModel> MountFacts { get; } = [];

    /// <summary>The container's resource limits, one fact row each.</summary>
    public ObservableCollection<FactRowViewModel> LimitFacts { get; } = [];

    /// <summary>The container's environment variables.</summary>
    public ObservableCollection<string> EnvironmentLines { get; } = [];

    /// <summary>Whether the environment block is expanded.</summary>
    public bool IsEnvironmentExpanded
    {
        get;
        set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(EnvironmentVisibility));
            NotifyPropertyChanged(nameof(EnvironmentToggleText));
        }
    }

    /// <summary>Whether the environment block is showing.</summary>
    public Visibility EnvironmentVisibility => GetVisibility(IsEnvironmentExpanded);

    /// <summary>The environment block's toggle caption.</summary>
    public string EnvironmentToggleText => IsEnvironmentExpanded
        ? "Hide environment (" + EnvironmentLines.Count.ToString(CultureInfo.InvariantCulture) + ")"
        : "Show environment (" + EnvironmentLines.Count.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>The Logs tab's text.</summary>
    public string LogText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>How many log lines to ask for.</summary>
    public ObservableCollection<string> LogTailOptions { get; } = [];

    /// <summary>The chosen log-tail size.</summary>
    public string LogTail
    {
        get;
        set
        {
            SetProperty(ref field, value ?? "500 lines");
            _ = LoadLogsAsync();
        }
    } = "500 lines";

    /// <summary>Whether the log lines carry timestamps.</summary>
    public bool LogTimestamps
    {
        get;
        set
        {
            SetProperty(ref field, value);
            _ = LoadLogsAsync();
        }
    }

    /// <summary>
    /// Whether the log view re-reads itself every two seconds. CodeBrix.Docker has no follow
    /// API, so this polls — the UI says so rather than pretending to stream.
    /// </summary>
    public bool LogAutoRefresh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (value) { StartLogPolling(); } else { StopLogPolling(); }
        }
    }

    /// <summary>The Stats tab's figures, as fact rows.</summary>
    public ObservableCollection<FactRowViewModel> StatFacts { get; } = [];

    /// <summary>The last sixty CPU samples, scaled for the sparkline strip.</summary>
    public ObservableCollection<SparkBarViewModel> CpuHistory { get; } = [];

    /// <summary>The CPU percentage, for its progress bar.</summary>
    public double CpuPercent
    {
        get;
        private set
        {
            //There is no double overload of SetProperty, so compare and notify by hand.
            if (Math.Abs(field - value) < 0.01d) { return; }
            field = value;
            NotifyPropertyChanged(nameof(CpuPercent));
        }
    }

    /// <summary>The memory percentage, for its progress bar.</summary>
    public double MemoryPercent
    {
        get;
        private set
        {
            //There is no double overload of SetProperty, so compare and notify by hand.
            if (Math.Abs(field - value) < 0.01d) { return; }
            field = value;
            NotifyPropertyChanged(nameof(MemoryPercent));
        }
    }

    /// <summary>The CPU readout beside its bar.</summary>
    public string CpuText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The memory readout beside its bar.</summary>
    public string MemoryText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The Diagnostics tab's cards.</summary>
    public ObservableCollection<DiagnosticCardViewModel> DiagnosticCards { get; } = [];

    /// <summary>The one-line diagnostics summary.</summary>
    public string DiagnosticsSummary
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Whether the container declares a healthcheck, so waiting for it makes sense.</summary>
    [AffectsCommands(nameof(WaitHealthyCommand))]
    public bool HasHealthcheck
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The Advisor tab's findings.</summary>
    public ObservableCollection<AdvisorFindingRowViewModel> Findings { get; } = [];

    /// <summary>Whether the advisor found nothing.</summary>
    public Visibility NoFindingsVisibility => GetVisibility(Findings.Count == 0);

    /// <summary>The signal the Kill button sends.</summary>
    public ObservableCollection<string> KillSignals { get; } = [];

    /// <summary>The chosen kill signal.</summary>
    public string KillSignal
    {
        get;
        set => SetProperty(ref field, value ?? "SIGKILL");
    } = "SIGKILL";

    /// <summary>Which tab is showing.</summary>
    public ContainerTab Tab => _tab;

    /// <summary>Whether the Overview tab is showing.</summary>
    public Visibility OverviewVisibility => GetVisibility(_tab == ContainerTab.Overview);

    /// <summary>Whether the Logs tab is showing.</summary>
    public Visibility LogsVisibility => GetVisibility(_tab == ContainerTab.Logs);

    /// <summary>Whether the Stats tab is showing.</summary>
    public Visibility StatsVisibility => GetVisibility(_tab == ContainerTab.Stats);

    /// <summary>Whether the Diagnostics tab is showing.</summary>
    public Visibility DiagnosticsTabVisibility => GetVisibility(_tab == ContainerTab.Diagnostics);

    /// <summary>Whether the Advisor tab is showing.</summary>
    public Visibility AdvisorVisibility => GetVisibility(_tab == ContainerTab.Advisor);

    /// <summary>The Overview tab button's underline colour.</summary>
    public Brush OverviewTabBrush => TabBrush(ContainerTab.Overview);

    /// <summary>The Logs tab button's underline colour.</summary>
    public Brush LogsTabBrush => TabBrush(ContainerTab.Logs);

    /// <summary>The Stats tab button's underline colour.</summary>
    public Brush StatsTabBrush => TabBrush(ContainerTab.Stats);

    /// <summary>The Diagnostics tab button's underline colour.</summary>
    public Brush DiagnosticsTabBrush => TabBrush(ContainerTab.Diagnostics);

    /// <summary>The Advisor tab button's underline colour.</summary>
    public Brush AdvisorTabBrush => TabBrush(ContainerTab.Advisor);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Shows one of the five tabs. The parameter is the tab's name.</summary>
    public SimpleCommand SelectTabCommand => field ??= new SimpleCommand(SelectTab);

    /// <summary>Starts the container.</summary>
    public SimpleCommand StartCommand => field ??= new SimpleCommand(
        () => !IsRunning && !IsBusy && HasSelection,
        (Func<Task>)(() => LifecycleAsync(() => _docker.StartContainerAsync(_containerId), "start")));

    /// <summary>Stops the container.</summary>
    public SimpleCommand StopCommand => field ??= new SimpleCommand(
        () => IsRunning && !IsBusy,
        (Func<Task>)(() => LifecycleAsync(() => _docker.StopContainerAsync(_containerId), "stop")));

    /// <summary>Restarts the container.</summary>
    public SimpleCommand RestartCommand => field ??= new SimpleCommand(
        () => IsRunning && !IsBusy,
        (Func<Task>)(() => LifecycleAsync(() => _docker.RestartContainerAsync(_containerId),
            "restart")));

    /// <summary>Sends the chosen signal to the container.</summary>
    public SimpleCommand KillCommand => field ??= new SimpleCommand(
        () => IsRunning && !IsBusy,
        (Func<Task>)(() => LifecycleAsync(
            () => _docker.KillContainerAsync(_containerId, KillSignal), "kill")));

    /// <summary>Removes the container, after confirming.</summary>
    public SimpleCommand RemoveCommand => field ??=
        new SimpleCommand(() => !IsBusy && HasSelection, (Func<Task>)RemoveAsync);

    /// <summary>Opens a console inside the container.</summary>
    public SimpleCommand ConsoleCommand => field ??= new SimpleCommand(
        () => IsRunning,
        () => _shell?.OpenConsole(_containerId, Title));

    /// <summary>Copies the container's full id.</summary>
    public SimpleCommand CopyIdCommand => field ??=
        new SimpleCommand(() => _shell?.CopyToClipboard(_containerId));

    /// <summary>Re-reads the log text now.</summary>
    public SimpleCommand RefreshLogsCommand => field ??= new SimpleCommand((Func<Task>)LoadLogsAsync);

    /// <summary>Copies the whole log text.</summary>
    public SimpleCommand CopyLogsCommand => field ??=
        new SimpleCommand(() => _shell?.CopyToClipboard(LogText));

    /// <summary>Shows or hides the environment block.</summary>
    public SimpleCommand ToggleEnvironmentCommand => field ??=
        new SimpleCommand(() => IsEnvironmentExpanded = !IsEnvironmentExpanded);

    /// <summary>Waits for the container's healthcheck to report healthy.</summary>
    public SimpleCommand WaitHealthyCommand => field ??= new SimpleCommand(
        () => HasHealthcheck && !IsBusy,
        (Func<Task>)WaitHealthyAsync);

    private void SelectTab(object parameter)
    {
        if (parameter is not string name
            || !Enum.TryParse<ContainerTab>(name, true, out var tab))
        {
            return;
        }

        if (_tab == tab) { return; }

        //Only one tab's live feed runs at a time; leaving Stats cancels its stream.
        StopStats();
        StopLogPolling();

        _tab = tab;
        NotifyPropertyChanged(nameof(Tab));
        NotifyPropertyChanged(nameof(OverviewVisibility));
        NotifyPropertyChanged(nameof(LogsVisibility));
        NotifyPropertyChanged(nameof(StatsVisibility));
        NotifyPropertyChanged(nameof(DiagnosticsTabVisibility));
        NotifyPropertyChanged(nameof(AdvisorVisibility));
        NotifyPropertyChanged(nameof(OverviewTabBrush));
        NotifyPropertyChanged(nameof(LogsTabBrush));
        NotifyPropertyChanged(nameof(StatsTabBrush));
        NotifyPropertyChanged(nameof(DiagnosticsTabBrush));
        NotifyPropertyChanged(nameof(AdvisorTabBrush));

        _ = LoadTabAsync();
    }

    private async Task RemoveAsync()
    {
        var confirmed = _shell is null || await _shell.ConfirmAsync(
            "Remove " + Title + "? Its writable layer goes with it.",
            "Remove container").ConfigureAwait(true);
        if (!confirmed) { return; }

        await LifecycleAsync(
            () => _docker.RemoveContainerAsync(_containerId, force: true), "remove")
            .ConfigureAwait(true);
    }

    private async Task WaitHealthyAsync()
    {
        await LifecycleAsync(
            () => _docker.WaitForHealthyAsync(_containerId, TimeSpan.FromSeconds(60)),
            "wait-healthy").ConfigureAwait(true);
    }

    private async Task LifecycleAsync(Func<Task> work, string verb)
    {
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            await work().ConfigureAwait(true);
            _shell?.LogAutomation("container " + verb + " " + Formatting.Trim(_containerId, 12)
                + ": OK");
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            _shell?.LogAutomation("container " + verb + ": ERROR - " + exception.Message);
        }
        finally
        {
            IsBusy = false;
            if (_refreshAll is not null)
            {
                await _refreshAll().ConfigureAwait(true);
            }
        }
    }

    #endregion

    /// <summary>Whether a container is selected.</summary>
    public bool HasSelection => !string.IsNullOrEmpty(_containerId);

    /// <summary>
    /// Shows a container. Everything the visible tab needs is fetched; the other tabs load when
    /// they are chosen.
    /// </summary>
    /// <param name="container">The container to show, or null to clear the pane.</param>
    /// <returns>A task that completes when the visible tab has loaded.</returns>
    public async Task ShowAsync(ContainerInfo container)
    {
        StopStats();
        StopLogPolling();

        if (container is null)
        {
            _containerId = null;
            Title = string.Empty;
            Subtitle = string.Empty;
            OverviewFacts.Clear();
            NotifyPropertyChanged(nameof(HasSelectionVisibility));
            NotifyPropertyChanged(nameof(NoSelectionVisibility));
            return;
        }

        _containerId = container.Id;
        Title = container.Name;
        Subtitle = container.Image + "   ·   " + container.ShortId;
        IsRunning = container.IsRunning;
        StateText = Formatting.OrDash(container.Status);
        StateBrush = container.IsRunning ? Palette.Good : Palette.Idle;
        ErrorText = string.Empty;
        NotifyPropertyChanged(nameof(HasSelectionVisibility));
        NotifyPropertyChanged(nameof(NoSelectionVisibility));

        await LoadTabAsync().ConfigureAwait(true);
    }

    /// <summary>Cancels every live feed this pane owns. The section calls it when it goes away.</summary>
    public void Suspend()
    {
        StopStats();
        StopLogPolling();
    }

    private async Task LoadTabAsync()
    {
        if (!HasSelection) { return; }

        switch (_tab)
        {
            case ContainerTab.Overview:
                await LoadOverviewAsync().ConfigureAwait(true);
                break;
            case ContainerTab.Logs:
                await LoadLogsAsync().ConfigureAwait(true);
                if (LogAutoRefresh) { StartLogPolling(); }
                break;
            case ContainerTab.Stats:
                StartStats();
                break;
            case ContainerTab.Diagnostics:
                await LoadDiagnosticsAsync().ConfigureAwait(true);
                break;
            case ContainerTab.Advisor:
                await LoadAdvisorAsync().ConfigureAwait(true);
                break;
            default:
                break;
        }
    }

    private async Task LoadOverviewAsync()
    {
        try
        {
            var detail = await _docker.InspectContainerAsync(_containerId).ConfigureAwait(true);
            BuildOverview(detail);
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private void BuildOverview(ContainerDetail detail)
    {
        OverviewFacts.Clear();
        NetworkFacts.Clear();
        MountFacts.Clear();
        LimitFacts.Clear();
        EnvironmentLines.Clear();

        OverviewFacts.Add(new FactRowViewModel("Id", detail.Id, true));
        OverviewFacts.Add(new FactRowViewModel("Image", detail.Image));
        OverviewFacts.Add(new FactRowViewModel("Created",
            Formatting.Relative(detail.Created) + "   " + Formatting.Clock(detail.Created)));
        OverviewFacts.Add(new FactRowViewModel("State", Formatting.OrDash(detail.StateStatus)));
        OverviewFacts.Add(new FactRowViewModel("Started", Formatting.Relative(detail.StartedAt)));
        if (!detail.IsRunning)
        {
            OverviewFacts.Add(new FactRowViewModel("Finished", Formatting.Relative(detail.FinishedAt)));
            OverviewFacts.Add(new FactRowViewModel("Exit code",
                detail.ExitCode.ToString(CultureInfo.InvariantCulture)));
        }
        OverviewFacts.Add(new FactRowViewModel("Restarts",
            detail.RestartCount.ToString(CultureInfo.InvariantCulture)));
        OverviewFacts.Add(new FactRowViewModel("Health",
            string.IsNullOrEmpty(detail.HealthStatus) ? "no healthcheck" : detail.HealthStatus));
        OverviewFacts.Add(new FactRowViewModel("Pid",
            detail.Pid == 0 ? "—" : detail.Pid.ToString(CultureInfo.InvariantCulture)));
        OverviewFacts.Add(new FactRowViewModel("Command",
            Formatting.Join(detail.Command), true));
        OverviewFacts.Add(new FactRowViewModel("Entrypoint",
            Formatting.Join(detail.Entrypoint), true));
        OverviewFacts.Add(new FactRowViewModel("Working directory",
            Formatting.OrDash(detail.WorkingDir), true));
        OverviewFacts.Add(new FactRowViewModel("User", Formatting.OrDash(detail.User)));
        OverviewFacts.Add(new FactRowViewModel("Hostname", Formatting.OrDash(detail.Hostname)));
        OverviewFacts.Add(new FactRowViewModel("Network mode",
            Formatting.OrDash(detail.NetworkMode)));
        if (!string.IsNullOrEmpty(detail.Error))
        {
            OverviewFacts.Add(new FactRowViewModel("Error", detail.Error));
        }

        foreach (var network in detail.Networks)
        {
            var aliases = network.Aliases.Count > 0
                ? "   aliases " + string.Join(", ", network.Aliases)
                : string.Empty;
            NetworkFacts.Add(new FactRowViewModel(network.NetworkName,
                Formatting.OrDash(network.IpAddress) + "   gateway "
                + Formatting.OrDash(network.Gateway) + aliases, true));
        }

        foreach (var mount in detail.Mounts)
        {
            MountFacts.Add(new FactRowViewModel(
                Formatting.OrDash(mount.Type) + (string.IsNullOrEmpty(mount.Name)
                    ? string.Empty : " " + mount.Name),
                Formatting.OrDash(mount.Source) + " → " + Formatting.OrDash(mount.Destination)
                + (mount.ReadWrite ? "  (rw)" : "  (ro)"), true));
        }

        var limits = detail.Limits;
        if (limits is not null)
        {
            LimitFacts.Add(new FactRowViewModel("CPUs", limits.Cpus.HasValue
                ? limits.Cpus.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "unlimited"));
            LimitFacts.Add(new FactRowViewModel("CPU set",
                Formatting.OrDash(limits.CpusetCpus)));
            LimitFacts.Add(new FactRowViewModel("CPU shares",
                limits.CpuShares.ToString(CultureInfo.InvariantCulture)));
            LimitFacts.Add(new FactRowViewModel("Memory", limits.HasMemoryLimit
                ? Formatting.Bytes(limits.MemoryBytes) : "unlimited"));
            LimitFacts.Add(new FactRowViewModel("Memory reservation",
                limits.MemoryReservationBytes > 0
                    ? Formatting.Bytes(limits.MemoryReservationBytes) : "none"));
            LimitFacts.Add(new FactRowViewModel("Swap",
                limits.IsSwapDisabled ? "disabled" : Formatting.Bytes(limits.MemorySwapBytes)));
            LimitFacts.Add(new FactRowViewModel("Process limit",
                limits.PidsLimit.HasValue
                    ? limits.PidsLimit.Value.ToString(CultureInfo.InvariantCulture)
                    : "unlimited"));
            LimitFacts.Add(new FactRowViewModel("Privileged", limits.Privileged ? "yes" : "no"));
            LimitFacts.Add(new FactRowViewModel("Restart policy",
                Formatting.OrDash(limits.RestartPolicy)));
            LimitFacts.Add(new FactRowViewModel("Log driver",
                Formatting.OrDash(limits.LogDriver)));
        }

        foreach (var variable in detail.Env)
        {
            EnvironmentLines.Add(variable);
        }
        NotifyPropertyChanged(nameof(EnvironmentToggleText));
    }

    private async Task LoadLogsAsync()
    {
        if (!HasSelection) { return; }

        try
        {
            int? tail = LogTail switch
            {
                "100 lines" => 100,
                "2000 lines" => 2000,
                "Everything" => null,
                _ => 500,
            };
            var logs = await _docker.GetLogsAsync(_containerId, tail, LogTimestamps)
                .ConfigureAwait(true);
            LogText = logs.IsEmpty ? "(no output)" : logs.Combined;
        }
        catch (Exception exception)
        {
            LogText = "Could not read the log: " + exception.Message;
        }
    }

    private void StartLogPolling()
    {
        StopLogPolling();
        if (!HasSelection || _tab != ContainerTab.Logs) { return; }

        var cancellation = new CancellationTokenSource();
        _logPolling = cancellation;
        _ = PollLogsAsync(cancellation.Token);
    }

    private async Task PollLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(2000, cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested) { return; }
                await LoadLogsAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            //Leaving the tab cancels the poll; that is not a failure.
        }
    }

    private void StopLogPolling()
    {
        var cancellation = _logPolling;
        _logPolling = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void StartStats()
    {
        StopStats();
        if (!HasSelection) { return; }

        CpuHistory.Clear();
        var cancellation = new CancellationTokenSource();
        _statsCancellation = cancellation;
        _ = PumpStatsAsync(cancellation.Token);
    }

    private async Task PumpStatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in _docker.StreamStatsAsync(_containerId, cancellationToken)
                .ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested) { return; }
                InvokeOnMainThread(() => ApplyStats(sample));
            }
        }
        catch (OperationCanceledException)
        {
            //The stream ends when the token is cancelled, which is how leaving the tab works.
        }
        catch (Exception exception)
        {
            InvokeOnMainThread(() => ErrorText = exception.Message);
        }
    }

    private void ApplyStats(ContainerStatsSample sample)
    {
        CpuPercent = Math.Min(100d, sample.CpuPercent ?? 0d);
        MemoryPercent = Math.Min(100d, sample.MemoryPercent ?? 0d);
        CpuText = Formatting.Percent(sample.CpuPercent);
        MemoryText = Formatting.Bytes(sample.MemoryUsageBytes) + " of "
            + Formatting.Bytes(sample.MemoryLimitBytes) + "   ("
            + Formatting.Percent(sample.MemoryPercent) + ")";

        CpuHistory.Add(new SparkBarViewModel(CpuPercent));
        while (CpuHistory.Count > 60)
        {
            CpuHistory.RemoveAt(0);
        }

        StatFacts.Clear();
        StatFacts.Add(new FactRowViewModel("Effective memory",
            Formatting.Percent(sample.EffectiveMemoryPercent)));
        StatFacts.Add(new FactRowViewModel("Anonymous", Formatting.Bytes(sample.AnonBytes)));
        StatFacts.Add(new FactRowViewModel("Page cache", Formatting.Bytes(sample.FileBytes)));
        StatFacts.Add(new FactRowViewModel("Processes",
            Formatting.Number(sample.PidsCurrent) + " of " + Formatting.Number(sample.PidsLimit)));
        StatFacts.Add(new FactRowViewModel("Network",
            "rx " + Formatting.Bytes(sample.NetworkRxBytes)
            + "   tx " + Formatting.Bytes(sample.NetworkTxBytes)));
        StatFacts.Add(new FactRowViewModel("Block I/O",
            "read " + Formatting.Bytes(sample.BlockReadBytes)
            + "   write " + Formatting.Bytes(sample.BlockWriteBytes)));
        StatFacts.Add(new FactRowViewModel("Throttling",
            Formatting.Percent(sample.ThrottleRatio.HasValue
                ? sample.ThrottleRatio.Value * 100d : null)));
        StatFacts.Add(new FactRowViewModel("Sampled", Formatting.Clock(sample.Timestamp)));
    }

    private void StopStats()
    {
        var cancellation = _statsCancellation;
        _statsCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task LoadDiagnosticsAsync()
    {
        DiagnosticCards.Clear();
        try
        {
            var report = await _docker.DiagnoseAsync(_containerId).ConfigureAwait(true);
            DiagnosticsSummary = Formatting.OrDash(report.Summary);
            HasHealthcheck = report.Health?.HasHealthcheck ?? false;

            if (report.Cpu is not null)
            {
                DiagnosticCards.Add(new DiagnosticCardViewModel("CPU throttling",
                    report.Cpu.Severity.ToString(), SeverityBrush(report.Cpu.Severity),
                    report.Cpu.Interpretation,
                    [
                        new FactRowViewModel("Periods",
                            Formatting.Number(report.Cpu.Periods)),
                        new FactRowViewModel("Throttled periods",
                            Formatting.Number(report.Cpu.ThrottledPeriods)),
                        new FactRowViewModel("Throttled time",
                            Formatting.Duration(report.Cpu.ThrottledTime)),
                        new FactRowViewModel("Ratio",
                            Formatting.Percent(report.Cpu.ThrottleRatio * 100d)),
                    ]));
            }
            if (report.Memory is not null)
            {
                DiagnosticCards.Add(new DiagnosticCardViewModel("Memory",
                    report.Memory.IsPageCacheDominated ? "PAGE CACHE" : "NORMAL",
                    report.Memory.IsPageCacheDominated ? Palette.Warn : Palette.TextTertiary,
                    report.Memory.Interpretation,
                    [
                        new FactRowViewModel("Usage",
                            Formatting.Bytes(report.Memory.UsageBytes)),
                        new FactRowViewModel("Limit", Formatting.Bytes(report.Memory.LimitBytes)),
                        new FactRowViewModel("Anonymous",
                            Formatting.Bytes(report.Memory.AnonBytes)),
                        new FactRowViewModel("Page cache",
                            Formatting.Bytes(report.Memory.FileBytes)),
                        new FactRowViewModel("Kernel",
                            Formatting.Bytes(report.Memory.KernelBytes)),
                        new FactRowViewModel("Effective",
                            Formatting.Percent(report.Memory.EffectiveUsagePercent)),
                    ]));
            }
            if (report.Oom is not null)
            {
                DiagnosticCards.Add(new DiagnosticCardViewModel("Out of memory",
                    report.Oom.WasOomKilled ? "KILLED" : "CLEAR",
                    report.Oom.WasOomKilled ? Palette.Bad : Palette.Good,
                    report.Oom.Interpretation,
                    [
                        new FactRowViewModel("Exit code",
                            report.Oom.ExitCode.ToString(CultureInfo.InvariantCulture)),
                        new FactRowViewModel("Restarts",
                            report.Oom.RestartCount.ToString(CultureInfo.InvariantCulture)),
                        new FactRowViewModel("Memory limit",
                            Formatting.Bytes(report.Oom.MemoryLimitBytes)),
                    ]));
            }
            if (report.Health is not null)
            {
                DiagnosticCards.Add(new DiagnosticCardViewModel("Health",
                    report.Health.HasHealthcheck
                        ? (report.Health.Status ?? "unknown").ToUpperInvariant()
                        : "NONE",
                    report.Health.IsHealthy ? Palette.Good : Palette.TextTertiary,
                    report.Health.Interpretation,
                    [
                        new FactRowViewModel("Failing streak",
                            report.Health.FailingStreak.ToString(CultureInfo.InvariantCulture)),
                        new FactRowViewModel("Recent checks",
                            report.Health.RecentChecks.Count
                                .ToString(CultureInfo.InvariantCulture)),
                    ]));
            }
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
    }

    private async Task LoadAdvisorAsync()
    {
        Findings.Clear();
        try
        {
            var findings = await _docker.AdviseContainerAsync(_containerId).ConfigureAwait(true);
            foreach (var finding in findings)
            {
                Findings.Add(new AdvisorFindingRowViewModel(finding));
            }
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        NotifyPropertyChanged(nameof(NoFindingsVisibility));
    }

    private static Brush SeverityBrush(ThrottleLevel level) => level switch
    {
        ThrottleLevel.Critical => Palette.Bad,
        ThrottleLevel.High => Palette.Bad,
        ThrottleLevel.Moderate => Palette.Warn,
        _ => Palette.Good,
    };

    private Brush TabBrush(ContainerTab tab) => _tab == tab ? Palette.Accent : Palette.Transparent;
}

/// <summary>One bar of the stats sparkline, whose height is the sample scaled to sixty pixels.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class SparkBarViewModel
{
    /// <summary>Creates a bar from a percentage.</summary>
    /// <param name="percent">The sample, from 0 to 100.</param>
    public SparkBarViewModel(double percent)
    {
        BarHeight = Math.Max(1d, Math.Min(60d, percent * 0.6d));
    }

    /// <summary>The bar's height in pixels.</summary>
    public double BarHeight { get; }
}

/// <summary>One card of the Diagnostics tab: a title, a severity chip, a sentence and its facts.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class DiagnosticCardViewModel
{
    /// <summary>Creates a diagnostics card.</summary>
    /// <param name="title">The card's title.</param>
    /// <param name="chipText">The severity chip's caption.</param>
    /// <param name="chipBrush">The severity chip's colour.</param>
    /// <param name="interpretation">The library's own sentence about what it found.</param>
    /// <param name="facts">The supporting numbers.</param>
    public DiagnosticCardViewModel(string title, string chipText, Brush chipBrush,
        string interpretation, IReadOnlyList<FactRowViewModel> facts)
    {
        Title = title;
        ChipText = chipText;
        ChipBrush = chipBrush;
        Interpretation = Formatting.OrDash(interpretation);
        foreach (var fact in facts)
        {
            Facts.Add(fact);
        }
    }

    /// <summary>The card's title.</summary>
    public string Title { get; }

    /// <summary>The severity chip's caption.</summary>
    public string ChipText { get; }

    /// <summary>The severity chip's colour.</summary>
    public Brush ChipBrush { get; }

    /// <summary>The library's own sentence about what it found.</summary>
    public string Interpretation { get; }

    /// <summary>The supporting numbers.</summary>
    public ObservableCollection<FactRowViewModel> Facts { get; } = [];
}
