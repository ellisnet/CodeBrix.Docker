using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// Section 8 — the daemon itself: its full description, what its storage is holding with the
/// prune buttons beside it, a live event stream, the whole advisor sweep, and the one
/// destructive action this tool offers over its own resources.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class SystemViewModel : SectionViewModel
{
    /// <summary>The event-filter entry that shows every event.</summary>
    public const string EventFilterAll = "All events";

    private readonly IDockerManager _docker;
    private readonly IRedisTopologyService _topologies;
    private CancellationTokenSource _eventStream;

    /// <summary>Creates the system section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public SystemViewModel(IShellContext shell)
        : base(shell)
    {
        _docker = GetService<IDockerManager>();
        _topologies = GetService<IRedisTopologyService>();

        EventFilterOptions.Add(EventFilterAll);
        EventFilterOptions.Add("container");
        EventFilterOptions.Add("image");
        EventFilterOptions.Add("network");
        EventFilterOptions.Add("volume");

        SeverityFilterOptions.Add("All severities");
        SeverityFilterOptions.Add("Critical");
        SeverityFilterOptions.Add("Warning");
        SeverityFilterOptions.Add("Info");
    }

    #region | Bindable properties |

    /// <summary>The daemon's description, in full.</summary>
    public ObservableCollection<FactRowViewModel> DaemonFacts { get; } = [];

    /// <summary>What the daemon's storage is holding.</summary>
    public ObservableCollection<DiskUsageRowViewModel> DiskRows { get; } = [];

    /// <summary>The daemon's live event stream, newest first, capped at five hundred lines.</summary>
    public ObservableCollection<EventRowViewModel> Events { get; } = [];

    /// <summary>The advisor's findings across the whole daemon.</summary>
    public ObservableCollection<AdvisorFindingRowViewModel> Findings { get; } = [];

    /// <summary>The event filter's choices.</summary>
    public ObservableCollection<string> EventFilterOptions { get; } = [];

    /// <summary>The advisor severity filter's choices.</summary>
    public ObservableCollection<string> SeverityFilterOptions { get; } = [];

    /// <summary>The chosen event filter.</summary>
    public string EventFilter
    {
        get;
        set => SetProperty(ref field, value ?? EventFilterAll);
    } = EventFilterAll;

    /// <summary>The chosen advisor severity filter.</summary>
    public string SeverityFilter
    {
        get;
        set
        {
            SetProperty(ref field, value ?? "All severities");
            RebuildFindings();
        }
    } = "All severities";

    /// <summary>Whether the event stream is paused.</summary>
    public bool IsEventStreamPaused
    {
        get;
        set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(PauseButtonText));
        }
    }

    /// <summary>The pause button's caption.</summary>
    public string PauseButtonText => IsEventStreamPaused ? "Resume stream" : "Pause stream";

    /// <summary>The total the disk-usage rows add up to.</summary>
    public string TotalDiskText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>How many findings the advisor sweep returned, as a caption.</summary>
    public string FindingsCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Whether the advisor found nothing.</summary>
    public Visibility NoFindingsVisibility => GetVisibility(Findings.Count == 0);

    /// <summary>How many events have arrived, as a caption.</summary>
    public string EventCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "waiting for events…";

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<Task>)(() => Shell.RefreshAsync()));

    /// <summary>Pauses or resumes the live event stream.</summary>
    public SimpleCommand TogglePauseCommand => field ??=
        new SimpleCommand(() => IsEventStreamPaused = !IsEventStreamPaused);

    /// <summary>Empties the event list.</summary>
    public SimpleCommand ClearEventsCommand => field ??= new SimpleCommand(() =>
    {
        Events.Clear();
        EventCountText = "cleared";
    });

    /// <summary>Removes every stopped container, after confirming.</summary>
    public SimpleCommand PruneContainersCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(() => PruneAsync(
            "Remove every stopped container on this daemon?", "Prune containers",
            () => _docker.PruneContainersAsync())));

    /// <summary>Removes every dangling image, after confirming.</summary>
    public SimpleCommand PruneImagesCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(() => PruneAsync(
            "Remove every dangling image on this daemon?", "Prune images",
            () => _docker.PruneImagesAsync(dangling: true))));

    /// <summary>Removes every unused network, after confirming.</summary>
    public SimpleCommand PruneNetworksCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(() => PruneAsync(
            "Remove every unused network on this daemon?", "Prune networks",
            () => _docker.PruneNetworksAsync())));

    /// <summary>Removes every unused volume, after confirming.</summary>
    public SimpleCommand PruneVolumesCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(() => PruneAsync(
            "Remove every unused volume on this daemon? Their contents go with them.",
            "Prune volumes", () => _docker.PruneVolumesAsync())));

    /// <summary>Destroys every RedisSetupTool instance, after listing what will go.</summary>
    public SimpleCommand SweepCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)SweepAsync);

    private async Task PruneAsync(string message, string title, Func<Task> work)
    {
        if (await Shell.ConfirmAsync(message, title).ConfigureAwait(true))
        {
            await RunAsync(work).ConfigureAwait(true);
        }
    }

    private async Task SweepAsync()
    {
        var snapshot = State?.Instances;
        if (snapshot is null || snapshot.Count == 0)
        {
            await Shell.ShowInfoAsync("There is nothing to sweep — no instances were found.")
                .ConfigureAwait(true);
            return;
        }

        var lines = new List<string>();
        var containers = 0;
        var volumes = 0;
        foreach (var instance in snapshot)
        {
            lines.Add("  " + instance.TopologyCode + "  " + instance.InstanceName + "  ("
                + Formatting.Plural(instance.NodeCount, "container") + ")");
            containers += instance.NodeCount;
            volumes += instance.VolumeNames.Count;
        }

        var confirmed = await Shell.ConfirmAsync(
            "Destroy every RedisSetupTool instance?\n\n" + string.Join("\n", lines)
            + "\n\nThat is " + Formatting.Plural(containers, "container") + " and "
            + Formatting.Plural(volumes, "volume") + ". Nothing else on the daemon is touched: "
            + "the sweep is scoped to this tool's own labels.",
            "Sweep RedisSetupTool resources").ConfigureAwait(true);
        if (!confirmed) { return; }

        Shell.PauseAutoRefresh();
        await RunAsync(async () =>
        {
            foreach (var instance in snapshot)
            {
                await _topologies.DestroyAsync(instance.InstanceId).ConfigureAwait(true);
                Shell.LogAutomation("sweep destroyed " + instance.InstanceId);
            }
        }).ConfigureAwait(true);
        Shell.ResumeAutoRefresh();
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var state = State;
        if (state is null) { return; }

        DaemonFacts.Clear();
        var daemon = state.Daemon;
        if (daemon is not null)
        {
            DaemonFacts.Add(new FactRowViewModel("Endpoint", daemon.Endpoint, true));
            DaemonFacts.Add(new FactRowViewModel("Reachable", daemon.IsReachable ? "yes" : "no"));
            DaemonFacts.Add(new FactRowViewModel("Server version", daemon.ServerVersion));
            DaemonFacts.Add(new FactRowViewModel("API version", daemon.ApiVersion));
            DaemonFacts.Add(new FactRowViewModel("Minimum API version",
                Formatting.OrDash(daemon.MinApiVersion)));
            DaemonFacts.Add(new FactRowViewModel("OS type", Formatting.OrDash(daemon.OsType)));
            DaemonFacts.Add(new FactRowViewModel("Operating system",
                Formatting.OrDash(daemon.OperatingSystem)));
            DaemonFacts.Add(new FactRowViewModel("Kernel",
                Formatting.OrDash(daemon.KernelVersion)));
            DaemonFacts.Add(new FactRowViewModel("Architecture",
                Formatting.OrDash(daemon.Architecture)));
            DaemonFacts.Add(new FactRowViewModel("cgroup version",
                Formatting.OrDash(daemon.CgroupVersion)));
            DaemonFacts.Add(new FactRowViewModel("cgroup driver",
                Formatting.OrDash(daemon.CgroupDriver)));
            DaemonFacts.Add(new FactRowViewModel("Storage driver",
                Formatting.OrDash(daemon.StorageDriver)));
            DaemonFacts.Add(new FactRowViewModel("Logging driver",
                Formatting.OrDash(daemon.LoggingDriver)));
            DaemonFacts.Add(new FactRowViewModel("CPUs", Formatting.Number(daemon.CpuCount)));
            DaemonFacts.Add(new FactRowViewModel("Total memory",
                Formatting.Bytes(daemon.TotalMemoryBytes)));
            DaemonFacts.Add(new FactRowViewModel("Containers",
                Formatting.Number(daemon.ContainerCount) + " ("
                + Formatting.Number(daemon.ContainersRunning) + " running, "
                + Formatting.Number(daemon.ContainersPaused) + " paused, "
                + Formatting.Number(daemon.ContainersStopped) + " stopped)"));
            DaemonFacts.Add(new FactRowViewModel("Images", Formatting.Number(daemon.ImageCount)));
            DaemonFacts.Add(new FactRowViewModel("Memory limit support",
                daemon.HasMemoryLimitSupport ? "yes" : "no"));
            DaemonFacts.Add(new FactRowViewModel("Swap limit support",
                daemon.HasSwapLimitSupport ? "yes" : "no"));
            foreach (var warning in daemon.Warnings)
            {
                DaemonFacts.Add(new FactRowViewModel("Warning", warning));
            }
        }

        DiskRows.Clear();
        var usage = state.DiskUsage;
        if (usage is not null)
        {
            var total = usage.TotalSizeBytes;
            DiskRows.Add(new DiskUsageRowViewModel("Images", usage.ImagesSizeBytes, total,
                Formatting.Plural(usage.ImageCount, "image") + ", "
                + usage.ReclaimableImageCount.ToString() + " reclaimable"));
            DiskRows.Add(new DiskUsageRowViewModel("Containers", usage.ContainersSizeBytes, total,
                Formatting.Plural(usage.ContainerCount, "container")));
            DiskRows.Add(new DiskUsageRowViewModel("Volumes", usage.VolumesSizeBytes, total,
                Formatting.Plural(usage.VolumeCount, "volume") + ", "
                + usage.ReclaimableVolumeCount.ToString() + " reclaimable"));
            DiskRows.Add(new DiskUsageRowViewModel("Build cache", usage.BuildCacheSizeBytes, total,
                Formatting.Bytes(usage.ReclaimableBuildCacheBytes) + " reclaimable"));
            DiskRows.Add(new DiskUsageRowViewModel("Layers", usage.LayersSizeBytes, total));
            TotalDiskText = Formatting.Bytes(total);
        }

        RebuildFindings();
    }

    /// <summary>
    /// Starts the daemon's event stream. The shell calls this once, and feeds every event both
    /// here and to the dashboard, so only one stream is ever open.
    /// </summary>
    /// <param name="daemonEvent">The event that just arrived.</param>
    public void AddEvent(DaemonEvent daemonEvent)
    {
        if (daemonEvent is null || IsEventStreamPaused) { return; }

        if (!string.Equals(EventFilter, EventFilterAll, StringComparison.Ordinal)
            && !string.Equals(daemonEvent.Type, EventFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Events.Insert(0, new EventRowViewModel(daemonEvent));
        while (Events.Count > 500)
        {
            Events.RemoveAt(Events.Count - 1);
        }
        EventCountText = Formatting.Plural(Events.Count, "event") + " since the app started";
    }

    /// <summary>Cancels anything this section owns. Kept for symmetry with the other sections.</summary>
    public void Suspend()
    {
        var cancellation = _eventStream;
        _eventStream = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void RebuildFindings()
    {
        Findings.Clear();
        var findings = State?.Findings;
        if (findings is null)
        {
            NotifyPropertyChanged(nameof(NoFindingsVisibility));
            return;
        }

        AdvisorLevel? wanted = SeverityFilter switch
        {
            "Critical" => AdvisorLevel.Critical,
            "Warning" => AdvisorLevel.Warning,
            "Info" => AdvisorLevel.Info,
            _ => null,
        };

        var sorted = new List<AdvisorFindingInfo>(findings);
        sorted.Sort((left, right) =>
        {
            var bySeverity = right.Severity.CompareTo(left.Severity);
            return bySeverity != 0
                ? bySeverity
                : string.CompareOrdinal(left.RuleId, right.RuleId);
        });

        foreach (var finding in sorted)
        {
            if (wanted.HasValue && finding.Severity != wanted.Value) { continue; }

            var containerName = finding.ContainerName;
            Findings.Add(new AdvisorFindingRowViewModel(finding,
                () => Shell?.ShowContainer(containerName)));
        }

        FindingsCountText = Findings.Count == findings.Count
            ? Formatting.Plural(findings.Count, "finding")
            : Findings.Count.ToString() + " of " + Formatting.Plural(findings.Count, "finding");
        NotifyPropertyChanged(nameof(NoFindingsVisibility));
    }
}
