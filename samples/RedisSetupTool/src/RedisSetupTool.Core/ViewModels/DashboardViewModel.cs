using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// Section 1 — the one screen that answers "what is running right now": the daemon's own
/// description, four counter cards that double as navigation, how the daemon's disk is being
/// used, and what the advisor thinks of it all.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class DashboardViewModel : SectionViewModel
{
    /// <summary>Creates the dashboard.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public DashboardViewModel(IShellContext shell)
        : base(shell)
    {
    }

    #region | Bindable properties |

    /// <summary>The daemon's description, as label/value rows.</summary>
    public ObservableCollection<FactRowViewModel> DaemonFacts { get; } = [];

    /// <summary>Whatever the daemon warned about at startup.</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>Whether the daemon raised any warnings.</summary>
    public Visibility WarningsVisibility => GetVisibility(Warnings.Count > 0);

    /// <summary>The five rows of the disk-usage card.</summary>
    public ObservableCollection<DiskUsageRowViewModel> DiskRows { get; } = [];

    /// <summary>The three highest advisor findings.</summary>
    public ObservableCollection<AdvisorFindingRowViewModel> TopFindings { get; } = [];

    /// <summary>The daemon's most recent events, newest first.</summary>
    public ObservableCollection<EventRowViewModel> RecentEvents { get; } = [];

    /// <summary>Whether any recent events have arrived yet.</summary>
    public Visibility RecentEventsVisibility => GetVisibility(RecentEvents.Count > 0);

    /// <summary>The Redis-instance counter card's headline, for example <c>2 / 3</c>.</summary>
    public string InstanceCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The container counter card's headline.</summary>
    public string ContainerCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The image counter card's headline.</summary>
    public string ImageCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The volume counter card's headline.</summary>
    public string VolumeCountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>The total the disk-usage rows add up to.</summary>
    public string TotalDiskText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>How many advisor findings are informational.</summary>
    public string AdvisorInfoText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0";

    /// <summary>How many advisor findings are warnings.</summary>
    public string AdvisorWarningText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0";

    /// <summary>How many advisor findings are critical.</summary>
    public string AdvisorCriticalText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "0";

    /// <summary>Whether the advisor found nothing at all.</summary>
    public Visibility NoFindingsVisibility => GetVisibility(TopFindings.Count == 0);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<System.Threading.Tasks.Task>)(() => Shell.RefreshAsync()));

    /// <summary>Shows the Redis Instances section.</summary>
    public SimpleCommand GoToInstancesCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.Instances));

    /// <summary>Shows the Containers section.</summary>
    public SimpleCommand GoToContainersCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.Containers));

    /// <summary>Shows the Images section.</summary>
    public SimpleCommand GoToImagesCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.Images));

    /// <summary>Shows the Networks and Volumes section.</summary>
    public SimpleCommand GoToVolumesCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.NetworksVolumes));

    /// <summary>Shows the create-instance form.</summary>
    public SimpleCommand NewInstanceCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.CreateInstance));

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var state = State;
        if (state is null) { return; }

        BuildDaemonFacts(state.Daemon);
        BuildCounters(state);
        BuildDiskRows(state.DiskUsage);
        BuildAdvisorSummary(state.Findings);
    }

    /// <summary>
    /// Adds one line to the recent-activity list, keeping only the newest twenty. The shell
    /// feeds this from its own event stream so the dashboard does not open a second one.
    /// </summary>
    /// <param name="daemonEvent">The event that just arrived.</param>
    public void AddEvent(DaemonEvent daemonEvent)
    {
        if (daemonEvent is null) { return; }

        RecentEvents.Insert(0, new EventRowViewModel(daemonEvent));
        while (RecentEvents.Count > 20)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }
        NotifyPropertyChanged(nameof(RecentEventsVisibility));
    }

    private void BuildDaemonFacts(DaemonInfo daemon)
    {
        DaemonFacts.Clear();
        Warnings.Clear();

        if (daemon is null)
        {
            DaemonFacts.Add(new FactRowViewModel("Daemon", "not reached"));
            NotifyPropertyChanged(nameof(WarningsVisibility));
            return;
        }

        DaemonFacts.Add(new FactRowViewModel("Endpoint", daemon.Endpoint, true));
        DaemonFacts.Add(new FactRowViewModel("Server version", daemon.ServerVersion));
        DaemonFacts.Add(new FactRowViewModel("API version",
            daemon.ApiVersion + " (minimum " + Formatting.OrDash(daemon.MinApiVersion) + ")"));
        DaemonFacts.Add(new FactRowViewModel("Operating system",
            Formatting.OrDash(daemon.OperatingSystem)));
        DaemonFacts.Add(new FactRowViewModel("Kernel", Formatting.OrDash(daemon.KernelVersion)));
        DaemonFacts.Add(new FactRowViewModel("Architecture", Formatting.OrDash(daemon.Architecture)));
        DaemonFacts.Add(new FactRowViewModel("cgroups",
            "v" + Formatting.OrDash(daemon.CgroupVersion) + " · " + Formatting.OrDash(daemon.CgroupDriver)));
        DaemonFacts.Add(new FactRowViewModel("Storage driver",
            Formatting.OrDash(daemon.StorageDriver)));
        DaemonFacts.Add(new FactRowViewModel("Logging driver",
            Formatting.OrDash(daemon.LoggingDriver)));
        DaemonFacts.Add(new FactRowViewModel("CPUs", Formatting.Number(daemon.CpuCount)));
        DaemonFacts.Add(new FactRowViewModel("Total memory", Formatting.Bytes(daemon.TotalMemoryBytes)));
        DaemonFacts.Add(new FactRowViewModel("Limit support",
            (daemon.HasMemoryLimitSupport ? "memory yes" : "memory no")
            + " · " + (daemon.HasSwapLimitSupport ? "swap yes" : "swap no")));

        foreach (var warning in daemon.Warnings)
        {
            Warnings.Add(warning);
        }
        NotifyPropertyChanged(nameof(WarningsVisibility));
    }

    private void BuildCounters(AppState state)
    {
        InstanceCountText = state.RunningInstanceCount.ToString() + " / "
            + state.Instances.Count.ToString();
        ContainerCountText = state.RunningContainerCount.ToString() + " / "
            + state.Containers.Count.ToString();
        ImageCountText = state.Images.Count.ToString();
        VolumeCountText = state.Volumes.Count.ToString();
    }

    private void BuildDiskRows(DaemonDiskUsage usage)
    {
        DiskRows.Clear();
        if (usage is null)
        {
            TotalDiskText = "—";
            return;
        }

        var total = usage.TotalSizeBytes;
        DiskRows.Add(new DiskUsageRowViewModel("Images", usage.ImagesSizeBytes, total,
            usage.ReclaimableImageCount > 0
                ? Formatting.Plural(usage.ReclaimableImageCount, "image") + " reclaimable"
                : null));
        DiskRows.Add(new DiskUsageRowViewModel("Containers", usage.ContainersSizeBytes, total,
            Formatting.Plural(usage.ContainerCount, "container")));
        DiskRows.Add(new DiskUsageRowViewModel("Volumes", usage.VolumesSizeBytes, total,
            usage.ReclaimableVolumeCount > 0
                ? Formatting.Plural(usage.ReclaimableVolumeCount, "volume") + " reclaimable"
                : null));
        DiskRows.Add(new DiskUsageRowViewModel("Build cache", usage.BuildCacheSizeBytes, total,
            usage.ReclaimableBuildCacheBytes > 0
                ? Formatting.Bytes(usage.ReclaimableBuildCacheBytes) + " reclaimable"
                : null));
        DiskRows.Add(new DiskUsageRowViewModel("Layers", usage.LayersSizeBytes, total));

        TotalDiskText = Formatting.Bytes(total);
    }

    private void BuildAdvisorSummary(IReadOnlyList<AdvisorFindingInfo> findings)
    {
        var info = 0;
        var warning = 0;
        var critical = 0;
        foreach (var finding in findings)
        {
            switch (finding.Severity)
            {
                case AdvisorLevel.Critical: critical++; break;
                case AdvisorLevel.Warning: warning++; break;
                default: info++; break;
            }
        }

        AdvisorInfoText = info.ToString();
        AdvisorWarningText = warning.ToString();
        AdvisorCriticalText = critical.ToString();

        TopFindings.Clear();
        var sorted = new List<AdvisorFindingInfo>(findings);
        sorted.Sort((left, right) => right.Severity.CompareTo(left.Severity));
        for (var index = 0; index < sorted.Count && index < 3; index++)
        {
            var finding = sorted[index];
            var containerName = finding.ContainerName;
            TopFindings.Add(new AdvisorFindingRowViewModel(finding,
                () => Shell?.ShowContainer(containerName)));
        }
        NotifyPropertyChanged(nameof(NoFindingsVisibility));
    }
}
