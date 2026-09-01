using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.RedisManagement;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// Section 2 — the centrepiece. Every Redis instance this tool created, one card each, with a
/// topology filter, a state filter and a search box above them. Cards survive a refresh so an
/// open Verify result is not thrown away every few seconds.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class InstancesViewModel : SectionViewModel
{
    /// <summary>The topology-filter entry that means "no filter".</summary>
    public const string AllTopologies = "All topologies";

    /// <summary>The state-filter entry that means "no filter".</summary>
    public const string AllStates = "Any state";

    private readonly IRedisTopologyService _topologies;
    private readonly IDockerManager _docker;
    private readonly IRedisProbe _probe;
    private readonly Dictionary<string, InstanceCardViewModel> _cards =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _rememberedParameters =
        new(StringComparer.Ordinal);

    /// <summary>Creates the instances section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public InstancesViewModel(IShellContext shell)
        : base(shell)
    {
        _topologies = GetService<IRedisTopologyService>();
        _docker = GetService<IDockerManager>();
        _probe = GetService<IRedisProbe>();

        TopologyFilterOptions.Add(AllTopologies);
        foreach (var descriptor in _topologies.Catalog)
        {
            TopologyFilterOptions.Add(descriptor.Code + " · " + descriptor.DisplayName);
        }

        StateFilterOptions.Add(AllStates);
        StateFilterOptions.Add("Running");
        StateFilterOptions.Add("Partial");
        StateFilterOptions.Add("Stopped");
    }

    #region | Bindable properties |

    /// <summary>The cards currently passing the filters.</summary>
    public ObservableCollection<InstanceCardViewModel> Instances { get; } = [];

    /// <summary>The topology filter's choices.</summary>
    public ObservableCollection<string> TopologyFilterOptions { get; } = [];

    /// <summary>The state filter's choices.</summary>
    public ObservableCollection<string> StateFilterOptions { get; } = [];

    /// <summary>The chosen topology filter.</summary>
    public string TopologyFilter
    {
        get;
        set
        {
            SetProperty(ref field, value ?? AllTopologies);
            ApplySnapshot();
        }
    } = AllTopologies;

    /// <summary>The chosen state filter.</summary>
    public string StateFilter
    {
        get;
        set
        {
            SetProperty(ref field, value ?? AllStates);
            ApplySnapshot();
        }
    } = AllStates;

    /// <summary>The free-text filter, matched against the name and the instance id.</summary>
    public string SearchText
    {
        get;
        set
        {
            SetProperty(ref field, value ?? string.Empty);
            ApplySnapshot();
        }
    } = string.Empty;

    /// <summary>How many instances are showing, as a caption.</summary>
    public string CountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Whether no cards are showing at all.</summary>
    public Visibility EmptyVisibility => GetVisibility(Instances.Count == 0);

    /// <summary>Whether at least one card is showing.</summary>
    public Visibility ListVisibility => GetVisibility(Instances.Count > 0);

    /// <summary>The empty state's message, which differs for "none yet" and "none matching".</summary>
    public string EmptyText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "No Redis instances yet.";

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<Task>)(() => Shell.RefreshAsync()));

    /// <summary>Shows the create-instance form.</summary>
    public SimpleCommand NewInstanceCommand => field ??=
        new SimpleCommand(() => Shell?.Navigate(SectionKey.CreateInstance));

    /// <summary>Clears every filter.</summary>
    public SimpleCommand ClearFiltersCommand => field ??= new SimpleCommand(() =>
    {
        TopologyFilter = AllTopologies;
        StateFilter = AllStates;
        SearchText = string.Empty;
    });

    /// <summary>Destroys every instance this tool created, after confirming.</summary>
    public SimpleCommand SweepAllCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)SweepAllAsync);

    private async Task SweepAllAsync()
    {
        var snapshot = State?.Instances;
        if (snapshot is null || snapshot.Count == 0)
        {
            await Shell.ShowInfoAsync("There is nothing to sweep — no instances were found.")
                .ConfigureAwait(true);
            return;
        }

        var names = new List<string>();
        foreach (var instance in snapshot)
        {
            names.Add(instance.InstanceName + " (" + instance.TopologyCode + ")");
        }

        var confirmed = await Shell.ConfirmAsync(
            "Destroy every RedisSetupTool instance?\n\n" + string.Join("\n", names)
            + "\n\nContainers, volumes and networks belonging to them are removed. "
            + "Nothing else on the daemon is touched.",
            "Sweep all instances").ConfigureAwait(true);
        if (!confirmed) { return; }

        Shell.PauseAutoRefresh();
        await RunAsync(async () =>
        {
            foreach (var instance in snapshot)
            {
                await _topologies.DestroyAsync(instance.InstanceId).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
        Shell.ResumeAutoRefresh();
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var snapshot = State?.Instances;
        if (snapshot is null) { return; }

        //Cards outlive a refresh: rebuilding them would discard an open Verify result and
        //  re-mask a password the user had just revealed.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in snapshot)
        {
            seen.Add(instance.InstanceId);
            if (_cards.TryGetValue(instance.InstanceId, out var existing))
            {
                existing.Update(instance);
            }
            else
            {
                var card = new InstanceCardViewModel(instance, Shell, _topologies, _docker, _probe);
                if (_rememberedParameters.TryGetValue(instance.InstanceId, out var parameters))
                {
                    card.RememberParameters(parameters);
                }
                _cards[instance.InstanceId] = card;
            }
        }

        var gone = new List<string>();
        foreach (var pair in _cards)
        {
            if (!seen.Contains(pair.Key)) { gone.Add(pair.Key); }
        }
        foreach (var key in gone)
        {
            _cards.Remove(key);
            _rememberedParameters.Remove(key);
        }

        Instances.Clear();
        foreach (var instance in snapshot)
        {
            if (!Matches(instance)) { continue; }

            var card = _cards[instance.InstanceId];
            Instances.Add(card);
            if (NeedsDiagnostics(instance.TopologyId))
            {
                _ = card.RefreshDiagnosticsAsync();
            }
        }

        CountText = Instances.Count == snapshot.Count
            ? Formatting.Plural(snapshot.Count, "instance")
            : Instances.Count.ToString() + " of " + Formatting.Plural(snapshot.Count, "instance");
        EmptyText = snapshot.Count == 0
            ? "No Redis instances yet. Create one from the topology catalog."
            : "No instance matches the current filters.";

        NotifyPropertyChanged(nameof(EmptyVisibility));
        NotifyPropertyChanged(nameof(ListVisibility));
    }

    /// <summary>
    /// Remembers the parameters a newly created instance was built with, so its card can verify
    /// the eviction policy the user chose. Labels do not carry it.
    /// </summary>
    /// <param name="instanceId">The new instance's id.</param>
    /// <param name="parameters">The parameter values the create form used.</param>
    public void RememberParameters(string instanceId,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(instanceId) || parameters is null) { return; }

        _rememberedParameters[instanceId] = parameters;
        if (_cards.TryGetValue(instanceId, out var card))
        {
            card.RememberParameters(parameters);
        }
    }

    /// <summary>Finds the card for an instance, or null when it is not on screen.</summary>
    /// <param name="instanceId">The instance id to look for.</param>
    /// <returns>The card, or null.</returns>
    public InstanceCardViewModel FindCard(string instanceId) =>
        instanceId is not null && _cards.TryGetValue(instanceId, out var card) ? card : null;

    private static bool NeedsDiagnostics(TopologyId topologyId) =>
        topologyId == TopologyId.G1;

    private bool Matches(TopologyInstance instance)
    {
        if (!string.Equals(TopologyFilter, AllTopologies, StringComparison.Ordinal)
            && !TopologyFilter.StartsWith(instance.TopologyCode + " ", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(StateFilter, AllStates, StringComparison.Ordinal))
        {
            var wanted = StateFilter switch
            {
                "Running" => InstanceState.Running,
                "Partial" => InstanceState.Partial,
                "Stopped" => InstanceState.Stopped,
                _ => InstanceState.Unknown,
            };
            if (instance.State != wanted) { return false; }
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            var name = instance.InstanceName ?? string.Empty;
            var id = instance.InstanceId ?? string.Empty;
            if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                && id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
