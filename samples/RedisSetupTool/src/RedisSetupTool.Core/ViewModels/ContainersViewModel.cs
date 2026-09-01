using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// Section 4 — every container on the daemon, not only this tool's. A filtered list on the left
/// and a five-tab detail pane on the right, with the whole container lifecycle in between.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ContainersViewModel : SectionViewModel
{
    /// <summary>The filter entry that shows every container.</summary>
    public const string FilterAll = "All containers";

    /// <summary>The filter entry that shows only running containers.</summary>
    public const string FilterRunning = "Running only";

    /// <summary>The filter entry that shows only containers this tool created.</summary>
    public const string FilterManaged = "Managed by this tool";

    private readonly IDockerManager _docker;
    private string _selectedId;

    /// <summary>Creates the containers section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public ContainersViewModel(IShellContext shell)
        : base(shell)
    {
        _docker = GetService<IDockerManager>();
        Detail = new ContainerDetailViewModel(shell, _docker, () => Shell.RefreshAsync());

        FilterOptions.Add(FilterAll);
        FilterOptions.Add(FilterRunning);
        FilterOptions.Add(FilterManaged);
    }

    #region | Bindable properties |

    /// <summary>The container rows passing the filters.</summary>
    public ObservableCollection<ContainerRowViewModel> Rows { get; } = [];

    /// <summary>The detail pane.</summary>
    public ContainerDetailViewModel Detail { get; }

    /// <summary>The filter's choices.</summary>
    public ObservableCollection<string> FilterOptions { get; } = [];

    /// <summary>The chosen filter.</summary>
    public string Filter
    {
        get;
        set
        {
            SetProperty(ref field, value ?? FilterAll);
            ApplySnapshot();
        }
    } = FilterAll;

    /// <summary>The free-text filter, matched against the name, image and id.</summary>
    public string SearchText
    {
        get;
        set
        {
            SetProperty(ref field, value ?? string.Empty);
            ApplySnapshot();
        }
    } = string.Empty;

    /// <summary>How many rows are showing, as a caption.</summary>
    public string CountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Whether the list is empty.</summary>
    public Visibility EmptyVisibility => GetVisibility(Rows.Count == 0);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<Task>)(() => Shell.RefreshAsync()));

    /// <summary>Removes every stopped container on the daemon, after confirming.</summary>
    public SimpleCommand PruneCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)PruneAsync);

    private async Task PruneAsync()
    {
        var confirmed = await Shell.ConfirmAsync(
            "Remove every stopped container on this daemon?\n\nThis is not limited to containers "
            + "this tool created — anything stopped goes.",
            "Prune stopped containers").ConfigureAwait(true);
        if (!confirmed) { return; }

        await RunAsync(() => _docker.PruneContainersAsync()).ConfigureAwait(true);
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var snapshot = State?.Containers;
        if (snapshot is null) { return; }

        Rows.Clear();
        ContainerRowViewModel selected = null;
        foreach (var container in snapshot)
        {
            if (!Matches(container)) { continue; }

            var row = new ContainerRowViewModel(container, SelectRow);
            if (string.Equals(container.Id, _selectedId, StringComparison.Ordinal))
            {
                row.IsSelected = true;
                selected = row;
            }
            Rows.Add(row);
        }

        CountText = Rows.Count == snapshot.Count
            ? Formatting.Plural(snapshot.Count, "container")
            : Rows.Count.ToString() + " of " + Formatting.Plural(snapshot.Count, "container");
        NotifyPropertyChanged(nameof(EmptyVisibility));

        if (selected is null && !string.IsNullOrEmpty(_selectedId))
        {
            //The selected container is gone (removed, or filtered out): clear the pane rather
            //  than leaving it showing something that no longer exists.
            _selectedId = null;
            _ = Detail.ShowAsync(null);
        }
    }

    /// <summary>
    /// Selects a container by id or name, which is how the dashboard's advisor rows and an
    /// instance card's node dots reach a container.
    /// </summary>
    /// <param name="idOrName">The container id or name to select.</param>
    public void SelectById(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) { return; }

        var snapshot = State?.Containers;
        if (snapshot is null) { return; }

        foreach (var container in snapshot)
        {
            if (string.Equals(container.Id, idOrName, StringComparison.Ordinal)
                || string.Equals(container.ShortId, idOrName, StringComparison.Ordinal)
                || string.Equals(container.Name, idOrName, StringComparison.Ordinal))
            {
                //A container reached this way may not pass the current filter, so widen it.
                if (!Matches(container))
                {
                    Filter = FilterAll;
                    SearchText = string.Empty;
                }
                _selectedId = container.Id;
                ApplySnapshot();
                _ = Detail.ShowAsync(container);
                return;
            }
        }
    }

    /// <summary>Cancels the detail pane's live feeds. The shell calls it when the section is hidden.</summary>
    public void Suspend() => Detail.Suspend();

    private void SelectRow(ContainerRowViewModel row)
    {
        if (row is null) { return; }

        foreach (var candidate in Rows)
        {
            candidate.IsSelected = ReferenceEquals(candidate, row);
        }
        _selectedId = row.Id;
        _ = Detail.ShowAsync(row.Info);
    }

    private bool Matches(ContainerInfo container)
    {
        if (string.Equals(Filter, FilterRunning, StringComparison.Ordinal) && !container.IsRunning)
        {
            return false;
        }
        if (string.Equals(Filter, FilterManaged, StringComparison.Ordinal) && !container.IsManaged)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            if (Contains(container.Name, needle) || Contains(container.Image, needle)
                || Contains(container.Id, needle))
            {
                return true;
            }
            return false;
        }

        return true;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack is not null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
