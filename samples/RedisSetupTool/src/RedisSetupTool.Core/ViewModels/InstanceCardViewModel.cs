using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.RedisManagement;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// One Redis instance, as a card: what it is, which of its nodes are up, everything a client
/// needs to connect, and the whole lifecycle — verify, console, logs, start, stop, restart and
/// destroy. A memory-capped instance (topology G1) additionally shows its diagnostics.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class InstanceCardViewModel : SimpleViewModel
{
    private readonly IShellContext _shell;
    private readonly IRedisTopologyService _topologies;
    private readonly IDockerManager _docker;
    private readonly IRedisProbe _probe;

    private TopologyInstance _instance;
    private IReadOnlyDictionary<string, string> _parameters;

    /// <summary>Creates a card over a discovered instance.</summary>
    /// <param name="instance">The instance to show.</param>
    /// <param name="shell">The shell, for navigation, the clipboard and dialogs.</param>
    /// <param name="topologies">The topology service that owns the lifecycle operations.</param>
    /// <param name="docker">The Docker facade, for the per-container operations.</param>
    /// <param name="probe">The Redis probe that answers Verify.</param>
    public InstanceCardViewModel(TopologyInstance instance, IShellContext shell,
        IRedisTopologyService topologies, IDockerManager docker, IRedisProbe probe)
    {
        _shell = shell;
        _topologies = topologies;
        _docker = docker;
        _probe = probe;
        Update(instance);
    }

    #region | Bindable properties |

    /// <summary>The instance's generated id, for example <c>a1-7f3c2b1d</c>.</summary>
    public string InstanceId
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The name the user gave the instance.</summary>
    public string InstanceName
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The two-character topology code shown in the chip, for example <c>C1</c>.</summary>
    public string TopologyCode
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>What the topology is, in a sentence.</summary>
    public string Summary
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The image and age line under the title.</summary>
    public string OriginText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The state pill's caption, for example <c>6 of 6 up</c>.</summary>
    public string StateText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The state pill's colour.</summary>
    public Brush StateBrush
    {
        get;
        private set => SetProperty(ref field, value);
    } = Palette.Idle;

    /// <summary>Whether every node of the instance is running.</summary>
    [AffectsAllCommands]
    public bool IsRunning
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Whether at least one node of the instance is stopped.</summary>
    [AffectsAllCommands]
    public bool IsStopped
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The instance's nodes, in creation order.</summary>
    public ObservableCollection<InstanceNodeViewModel> Nodes { get; } = [];

    /// <summary>The copyable rows of the CONNECT block.</summary>
    public ObservableCollection<EndpointRowViewModel> ConnectionRows { get; } = [];

    /// <summary>Anything the topology wants a client to know, for example a cluster caveat.</summary>
    public ObservableCollection<string> Notes { get; } = [];

    /// <summary>Whether there are notes to show.</summary>
    public Visibility NotesVisibility => GetVisibility(Notes.Count > 0);

    /// <summary>Whether the card carries a password at all.</summary>
    public bool HasPassword
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(PasswordWarningVisibility));
        }
    }

    /// <summary>Whether the card carries a password, so the local-development warning applies.</summary>
    public Visibility PasswordWarningVisibility => GetVisibility(HasPassword);

    /// <summary>The result of the last Verify, one row per check.</summary>
    public ObservableCollection<VerificationCheckViewModel> VerifyChecks { get; } = [];

    /// <summary>Whether the Verify result block is showing.</summary>
    public Visibility VerifyVisibility => GetVisibility(VerifyChecks.Count > 0);

    /// <summary>The one-line summary of the last Verify.</summary>
    public string VerifySummary
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Green when the last Verify passed, red when it did not.</summary>
    public Brush VerifyBrush
    {
        get;
        private set => SetProperty(ref field, value);
    } = Palette.TextSecondary;

    /// <summary>Whether a Verify is in flight.</summary>
    [AffectsAllCommands]
    public bool IsBusy
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The last thing that went wrong on this card, or an empty string.</summary>
    public string ErrorText
    {
        get;
        private set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>Whether an error line is showing.</summary>
    public Visibility ErrorVisibility => GetVisibility(!string.IsNullOrEmpty(ErrorText));

    /// <summary>The diagnostics strip, shown only for a resource-capped instance.</summary>
    public ObservableCollection<FactRowViewModel> DiagnosticsRows { get; } = [];

    /// <summary>Whether the diagnostics strip is showing.</summary>
    public Visibility DiagnosticsVisibility => GetVisibility(DiagnosticsRows.Count > 0);

    /// <summary>How much of the container's memory limit the instance is using, from 0 to 100.</summary>
    public double MemoryShare
    {
        get;
        private set
        {
            //There is no double overload of SetProperty, so compare and notify by hand.
            if (Math.Abs(field - value) < 0.01d) { return; }
            field = value;
            NotifyPropertyChanged(nameof(MemoryShare));
        }
    }

    #endregion

    #region | Commands and their implementations |

    /// <summary>Copies every connection line at once.</summary>
    public SimpleCommand CopyAllCommand => field ??= new SimpleCommand(CopyAll);

    /// <summary>Connects a real Redis client and reports what it found.</summary>
    public SimpleCommand VerifyCommand => field ??=
        new SimpleCommand(() => IsRunning && !IsBusy, (Func<Task>)VerifyAsync);

    /// <summary>Opens a console on the instance's first running node.</summary>
    public SimpleCommand ConsoleCommand => field ??=
        new SimpleCommand(() => IsRunning, OpenConsole);

    /// <summary>Shows the first node's container, where its logs live.</summary>
    public SimpleCommand LogsCommand => field ??= new SimpleCommand(ShowFirstContainer);

    /// <summary>Starts every stopped node.</summary>
    public SimpleCommand StartCommand => field ??=
        new SimpleCommand(() => IsStopped && !IsBusy, (Func<Task>)StartAsync);

    /// <summary>Stops every running node.</summary>
    public SimpleCommand StopCommand => field ??=
        new SimpleCommand(() => !IsStopped && !IsBusy, (Func<Task>)StopAsync);

    /// <summary>Restarts every node.</summary>
    public SimpleCommand RestartCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)RestartAsync);

    /// <summary>Removes the instance's containers, volumes and network, after confirming.</summary>
    public SimpleCommand DestroyCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)DestroyAsync);

    private void CopyAll()
    {
        var text = _instance?.Connection?.ConnectionString;
        if (!string.IsNullOrEmpty(text))
        {
            _shell?.CopyToClipboard(text);
        }
    }

    private void OpenConsole()
    {
        foreach (var node in Nodes)
        {
            if (node.IsRunning)
            {
                _shell?.OpenConsole(node.ContainerId, node.ContainerName);
                return;
            }
        }
    }

    private void ShowFirstContainer()
    {
        if (Nodes.Count > 0)
        {
            _shell?.ShowContainer(Nodes[0].ContainerId);
        }
    }

    private async Task VerifyAsync()
    {
        IsBusy = true;
        SetError(null);
        VerifyChecks.Clear();
        VerifySummary = "Connecting a Redis client…";
        VerifyBrush = Palette.TextSecondary;
        NotifyPropertyChanged(nameof(VerifyVisibility));

        try
        {
            var descriptor = ConnectionMapper.Map(_instance, _parameters);
            if (descriptor is null)
            {
                VerifySummary = "This instance carries no connection information.";
                VerifyBrush = Palette.Warn;
                return;
            }

            var verification = await _probe.VerifyAsync(descriptor).ConfigureAwait(true);
            foreach (var check in verification.Checks)
            {
                VerifyChecks.Add(new VerificationCheckViewModel(check));
            }
            VerifySummary = Formatting.OrDash(verification.Summary);
            VerifyBrush = verification.Succeeded ? Palette.Good : Palette.Bad;
            _shell?.LogAutomation("verify " + InstanceId + ": "
                + (verification.Succeeded ? "PASS" : "FAIL") + " - " + verification.Summary);
        }
        catch (Exception exception)
        {
            VerifySummary = "Verification could not run.";
            VerifyBrush = Palette.Bad;
            SetError(exception.Message);
            _shell?.LogAutomation("verify " + InstanceId + ": ERROR - " + exception.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyPropertyChanged(nameof(VerifyVisibility));
        }
    }

    private Task StartAsync() => LifecycleAsync(() => _topologies.StartAsync(InstanceId), "start");

    private Task StopAsync() => LifecycleAsync(() => _topologies.StopAsync(InstanceId), "stop");

    private Task RestartAsync() =>
        LifecycleAsync(() => _topologies.RestartAsync(InstanceId), "restart");

    private async Task DestroyAsync()
    {
        var volumeCount = _instance?.VolumeNames?.Count ?? 0;
        var networkCount = string.IsNullOrEmpty(_instance?.NetworkName) ? 0 : 1;
        var message = "Destroy " + InstanceName + "? This removes "
            + Formatting.Plural(Nodes.Count, "container") + ", "
            + Formatting.Plural(volumeCount, "volume") + " and "
            + Formatting.Plural(networkCount, "network") + ".";

        var confirmed = _shell is null
            || await _shell.ConfirmAsync(message, "Destroy instance").ConfigureAwait(true);
        if (!confirmed) { return; }

        await LifecycleAsync(() => _topologies.DestroyAsync(InstanceId), "destroy")
            .ConfigureAwait(true);
    }

    private async Task LifecycleAsync(Func<Task> work, string verb)
    {
        IsBusy = true;
        SetError(null);
        _shell?.PauseAutoRefresh();
        try
        {
            await work().ConfigureAwait(true);
            _shell?.LogAutomation(verb + " " + InstanceId + ": OK");
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            _shell?.LogAutomation(verb + " " + InstanceId + ": ERROR - " + exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell?.ResumeAutoRefresh();
            if (_shell is not null)
            {
                await _shell.RefreshAsync().ConfigureAwait(true);
            }
        }
    }

    #endregion

    /// <summary>
    /// Re-reads the card from a fresh discovery of the same instance. The card object survives
    /// a refresh so an open Verify result and a revealed password are not thrown away every few
    /// seconds.
    /// </summary>
    /// <param name="instance">The freshly discovered instance.</param>
    public void Update(TopologyInstance instance)
    {
        if (instance is null) { return; }

        _instance = instance;
        InstanceId = instance.InstanceId;
        InstanceName = Formatting.OrDash(instance.InstanceName);
        TopologyCode = instance.TopologyCode;

        var descriptor = SafeDescribe(instance.TopologyId);
        Summary = descriptor is null
            ? Formatting.OrDash(instance.StatusText)
            : descriptor.DisplayName + " — " + descriptor.Summary;
        OriginText = Formatting.OrDash(instance.Image) + "   created "
            + Formatting.Relative(instance.CreatedAt);

        IsRunning = instance.State == InstanceState.Running;
        IsStopped = instance.RunningNodeCount == 0;
        StateText = instance.RunningNodeCount.ToString(CultureInfo.InvariantCulture) + " of "
            + instance.NodeCount.ToString(CultureInfo.InvariantCulture) + " up";
        StateBrush = instance.State switch
        {
            InstanceState.Running => Palette.Good,
            InstanceState.Partial => Palette.Warn,
            InstanceState.Failed => Palette.Bad,
            InstanceState.Creating => Palette.Warn,
            _ => Palette.Idle,
        };

        RebuildNodes(instance);
        RebuildConnectionRows(instance);
    }

    /// <summary>
    /// Remembers the parameter values an instance was created with, so the verification can
    /// assert the eviction policy the user chose. A discovered instance carries no label for it.
    /// </summary>
    /// <param name="parameters">The parameters the create form used.</param>
    public void RememberParameters(IReadOnlyDictionary<string, string> parameters) =>
        _parameters = parameters;

    /// <summary>
    /// Asks the daemon for the first node's diagnostics and fills in the card's diagnostics
    /// strip. The Instances section calls this only for the topologies whose resource limits
    /// make it interesting; a failure quietly leaves the strip empty.
    /// </summary>
    /// <returns>A task that completes when the strip has been filled in.</returns>
    public async Task RefreshDiagnosticsAsync()
    {
        if (Nodes.Count == 0 || !Nodes[0].IsRunning)
        {
            ApplyDiagnostics(null);
            return;
        }

        try
        {
            var report = await _docker.DiagnoseAsync(Nodes[0].ContainerId).ConfigureAwait(true);
            ApplyDiagnostics(report);
        }
        catch (Exception)
        {
            //Diagnostics are a bonus strip on the card, not something worth an error line.
            ApplyDiagnostics(null);
        }
    }

    /// <summary>Fills in the diagnostics strip from an already-fetched report.</summary>
    /// <param name="report">The diagnostics report for the instance's first node, or null.</param>
    public void ApplyDiagnostics(DiagnosticsReport report)
    {
        DiagnosticsRows.Clear();
        if (report is null)
        {
            MemoryShare = 0d;
            NotifyPropertyChanged(nameof(DiagnosticsVisibility));
            return;
        }

        var memory = report.Memory;
        if (memory is not null)
        {
            DiagnosticsRows.Add(new FactRowViewModel("Memory",
                Formatting.Bytes(memory.UsageBytes) + " of " + Formatting.Bytes(memory.LimitBytes)
                + "  (" + Formatting.Percent(memory.UsagePercent) + ")"));
            MemoryShare = memory.UsagePercent ?? 0d;
        }
        if (report.Cpu is not null)
        {
            DiagnosticsRows.Add(new FactRowViewModel("CPU throttling",
                report.Cpu.Severity.ToString() + " — " + Formatting.OrDash(report.Cpu.Interpretation)));
        }
        if (report.Oom is not null)
        {
            DiagnosticsRows.Add(new FactRowViewModel("Out of memory",
                report.Oom.WasOomKilled ? "killed" : "no"));
        }
        if (report.Health is not null)
        {
            DiagnosticsRows.Add(new FactRowViewModel("Health",
                report.Health.HasHealthcheck ? Formatting.OrDash(report.Health.Status)
                    : "no healthcheck"));
        }

        NotifyPropertyChanged(nameof(DiagnosticsVisibility));
    }

    private TopologyDescriptor SafeDescribe(TopologyId topologyId)
    {
        try
        {
            return _topologies?.Describe(topologyId);
        }
        catch (Exception)
        {
            //An instance whose label names a topology this build does not know still shows.
            return null;
        }
    }

    private void RebuildNodes(TopologyInstance instance)
    {
        Nodes.Clear();
        foreach (var node in instance.Nodes)
        {
            Nodes.Add(new InstanceNodeViewModel(node,
                id => _shell?.ShowContainer(id),
                (id, name) => _shell?.OpenConsole(id, name)));
        }
    }

    private void RebuildConnectionRows(TopologyInstance instance)
    {
        ConnectionRows.Clear();
        Notes.Clear();

        var connection = instance.Connection;
        if (connection is null)
        {
            HasPassword = false;
            NotifyPropertyChanged(nameof(NotesVisibility));
            return;
        }

        void Copy(string text) => _shell?.CopyToClipboard(text);

        if (!string.IsNullOrEmpty(connection.ServiceName))
        {
            ConnectionRows.Add(new EndpointRowViewModel("service", connection.ServiceName, Copy));
        }

        var dataPorts = new List<string>();
        var sentinelPorts = new List<string>();
        foreach (var endpoint in connection.Endpoints)
        {
            if (endpoint.IsSentinel)
            {
                sentinelPorts.Add(endpoint.ToString());
            }
            else
            {
                dataPorts.Add(endpoint.ToString());
            }
        }
        if (dataPorts.Count > 0)
        {
            ConnectionRows.Add(new EndpointRowViewModel(
                dataPorts.Count == 1 ? "endpoint" : "endpoints",
                string.Join(",", dataPorts), Copy));
        }
        if (sentinelPorts.Count > 0)
        {
            ConnectionRows.Add(new EndpointRowViewModel("sentinels",
                string.Join(",", sentinelPorts), Copy));
        }

        if (!string.IsNullOrEmpty(connection.Username)
            && !string.Equals(connection.Username, "default", StringComparison.Ordinal))
        {
            ConnectionRows.Add(new EndpointRowViewModel("username", connection.Username, Copy));
        }
        if (!string.IsNullOrEmpty(connection.Password))
        {
            ConnectionRows.Add(EndpointRowViewModel.Secret("password", connection.Password, Copy));
        }
        foreach (var user in connection.AdditionalUsers)
        {
            ConnectionRows.Add(EndpointRowViewModel.Secret("user " + user.Username,
                user.Password, Copy));
        }
        if (!string.IsNullOrEmpty(connection.ConnectionString))
        {
            ConnectionRows.Add(new EndpointRowViewModel("string",
                connection.ConnectionString, Copy));
        }
        if (!string.IsNullOrEmpty(connection.CliCommand))
        {
            ConnectionRows.Add(new EndpointRowViewModel("redis-cli", connection.CliCommand, Copy));
        }

        foreach (var note in connection.Notes)
        {
            Notes.Add(note);
        }

        HasPassword = !string.IsNullOrEmpty(connection.Password);
        NotifyPropertyChanged(nameof(NotesVisibility));
    }

    private void SetError(string message)
    {
        ErrorText = message ?? string.Empty;
        NotifyPropertyChanged(nameof(ErrorVisibility));
    }
}
