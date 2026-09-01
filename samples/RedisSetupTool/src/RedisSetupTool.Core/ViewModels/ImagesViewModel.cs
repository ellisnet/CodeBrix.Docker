using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>One row of the image list.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ImageRowViewModel : SimpleViewModel
{
    private readonly Action<ImageRowViewModel> _select;
    private bool _isSelected;

    /// <summary>Wraps one image.</summary>
    /// <param name="image">The image to show.</param>
    /// <param name="select">What selecting the row does.</param>
    public ImageRowViewModel(ImageInfo image, Action<ImageRowViewModel> select)
    {
        Info = image;
        _select = select;
        DisplayName = Formatting.OrDash(image.DisplayName);
        ShortId = Formatting.OrDash(image.ShortId);
        SizeText = Formatting.Bytes(image.SizeBytes);
        CreatedText = Formatting.Relative(image.Created);
        ContainerText = image.ContainerCount > 0
            ? Formatting.Plural((int)image.ContainerCount, "container")
            : "unused";
        IsDangling = image.IsDangling;
    }

    /// <summary>The image this row shows.</summary>
    public ImageInfo Info { get; }

    /// <summary>The repository and tag, or a digest when there is no tag.</summary>
    public string DisplayName { get; }

    /// <summary>The image's short id.</summary>
    public string ShortId { get; }

    /// <summary>The image's size.</summary>
    public string SizeText { get; }

    /// <summary>How long ago the image was built.</summary>
    public string CreatedText { get; }

    /// <summary>How many containers use the image.</summary>
    public string ContainerText { get; }

    /// <summary>Whether the image has no tags at all.</summary>
    public bool IsDangling { get; }

    /// <summary>Whether the dangling chip is showing.</summary>
    public Visibility DanglingVisibility => GetVisibility(IsDangling);

    /// <summary>Whether this is the selected row.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) { return; }
            _isSelected = value;
            NotifyPropertyChanged(nameof(IsSelected));
            NotifyPropertyChanged(nameof(RowBackground));
        }
    }

    /// <summary>The row's background: raised while selected.</summary>
    public Brush RowBackground => _isSelected ? Palette.Raised : Palette.Transparent;

    /// <summary>Selects this row.</summary>
    public SimpleCommand SelectCommand => field ??= new SimpleCommand(() => _select?.Invoke(this));
}

/// <summary>One vulnerability from an image scan.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public sealed class VulnerabilityRowViewModel
{
    /// <summary>Wraps one vulnerability.</summary>
    /// <param name="vulnerability">The finding to show.</param>
    public VulnerabilityRowViewModel(VulnerabilityInfo vulnerability)
    {
        Id = vulnerability.Id;
        Severity = (vulnerability.Severity ?? "UNKNOWN").ToUpperInvariant();
        Package = vulnerability.PackageName + " " + vulnerability.InstalledVersion;
        Fix = vulnerability.HasFix ? "fixed in " + vulnerability.FixedVersion : "no fix";
        Title = Formatting.OrDash(vulnerability.Title);
        SeverityBrush = Severity switch
        {
            "CRITICAL" => Palette.Bad,
            "HIGH" => Palette.Bad,
            "MEDIUM" => Palette.Warn,
            _ => Palette.TextTertiary,
        };
    }

    /// <summary>The advisory's identifier.</summary>
    public string Id { get; }

    /// <summary>How serious it is.</summary>
    public string Severity { get; }

    /// <summary>The severity chip's colour.</summary>
    public Brush SeverityBrush { get; }

    /// <summary>The affected package and version.</summary>
    public string Package { get; }

    /// <summary>Whether a fixed version exists.</summary>
    public string Fix { get; }

    /// <summary>The advisory's title.</summary>
    public string Title { get; }
}

/// <summary>
/// Section 6 — every image on the daemon. A list on the left, and on the right the image's
/// details, its layers, and the four containerised analysis tools CodeBrix.Docker wraps.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class ImagesViewModel : SectionViewModel
{
    private readonly IDockerManager _docker;
    private string _selectedReference;

    /// <summary>Creates the images section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public ImagesViewModel(IShellContext shell)
        : base(shell)
    {
        _docker = GetService<IDockerManager>();
    }

    #region | Bindable properties |

    /// <summary>The image rows passing the filter.</summary>
    public ObservableCollection<ImageRowViewModel> Rows { get; } = [];

    /// <summary>The free-text filter.</summary>
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

    /// <summary>Whether an image is selected.</summary>
    public Visibility HasSelectionVisibility =>
        GetVisibility(!string.IsNullOrEmpty(_selectedReference));

    /// <summary>Whether nothing is selected.</summary>
    public Visibility NoSelectionVisibility =>
        GetVisibility(string.IsNullOrEmpty(_selectedReference));

    /// <summary>The selected image's name.</summary>
    public string Title
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The selected image's size and age.</summary>
    public string Subtitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The selected image's facts.</summary>
    public ObservableCollection<FactRowViewModel> OverviewFacts { get; } = [];

    /// <summary>The selected image's layers.</summary>
    public ObservableCollection<FactRowViewModel> Layers { get; } = [];

    /// <summary>The reference the Pull button pulls.</summary>
    [AffectsCommands(nameof(PullCommand))]
    public string PullReference
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>The tag the Tag button applies to the selected image.</summary>
    [AffectsCommands(nameof(TagCommand))]
    public string NewTag
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>The Dockerfile the Lint button reads.</summary>
    [AffectsCommands(nameof(LintCommand))]
    public string DockerfilePath
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>The running output of a pull, a scan or a lint.</summary>
    public ObservableCollection<string> ToolOutput { get; } = [];

    /// <summary>Whether the tool-output block is showing.</summary>
    public Visibility ToolOutputVisibility => GetVisibility(ToolOutput.Count > 0);

    /// <summary>The tool-output block's heading.</summary>
    public string ToolHeading
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The vulnerabilities the last scan found.</summary>
    public ObservableCollection<VulnerabilityRowViewModel> Vulnerabilities { get; } = [];

    /// <summary>Whether the vulnerability list is showing.</summary>
    public Visibility VulnerabilitiesVisibility => GetVisibility(Vulnerabilities.Count > 0);

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<Task>)(() => Shell.RefreshAsync()));

    /// <summary>Pulls the reference in the pull box.</summary>
    public SimpleCommand PullCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrWhiteSpace(PullReference), (Func<Task>)PullAsync);

    /// <summary>Applies the new tag to the selected image.</summary>
    public SimpleCommand TagCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrWhiteSpace(NewTag)
            && !string.IsNullOrEmpty(_selectedReference),
        (Func<Task>)TagAsync);

    /// <summary>Removes the selected image, after confirming.</summary>
    public SimpleCommand RemoveCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrEmpty(_selectedReference), (Func<Task>)RemoveAsync);

    /// <summary>Removes every dangling image, after confirming.</summary>
    public SimpleCommand PruneCommand => field ??=
        new SimpleCommand(() => !IsBusy, (Func<Task>)PruneAsync);

    /// <summary>Scans the selected image with Trivy.</summary>
    public SimpleCommand ScanCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrEmpty(_selectedReference), (Func<Task>)ScanAsync);

    /// <summary>Analyses the selected image's layer efficiency with Dive.</summary>
    public SimpleCommand EfficiencyCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrEmpty(_selectedReference), (Func<Task>)EfficiencyAsync);

    /// <summary>Lints the Dockerfile in the path box with Hadolint.</summary>
    public SimpleCommand LintCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrWhiteSpace(DockerfilePath), (Func<Task>)LintAsync);

    /// <summary>Clears the tool-output block.</summary>
    public SimpleCommand ClearOutputCommand => field ??= new SimpleCommand(() =>
    {
        ToolOutput.Clear();
        Vulnerabilities.Clear();
        ToolHeading = string.Empty;
        NotifyPropertyChanged(nameof(ToolOutputVisibility));
        NotifyPropertyChanged(nameof(VulnerabilitiesVisibility));
    });

    private async Task PullAsync()
    {
        BeginTool("Pulling " + PullReference);
        var reference = PullReference.Trim();
        var progress = new Progress<string>(line => InvokeOnMainThread(() => AppendOutput(line)));
        await RunAsync(() => _docker.PullImageAsync(reference, progress)).ConfigureAwait(true);
        AppendOutput("done.");
    }

    private async Task TagAsync()
    {
        BeginTool("Tagging " + _selectedReference + " as " + NewTag);
        var target = NewTag.Trim();
        var source = _selectedReference;
        await RunAsync(() => _docker.TagImageAsync(source, target)).ConfigureAwait(true);
        AppendOutput("done.");
    }

    private async Task RemoveAsync()
    {
        var confirmed = await Shell.ConfirmAsync(
            "Remove " + Title + "?", "Remove image").ConfigureAwait(true);
        if (!confirmed) { return; }

        var reference = _selectedReference;
        await RunAsync(() => _docker.RemoveImageAsync(reference, force: true)).ConfigureAwait(true);
    }

    private async Task PruneAsync()
    {
        var confirmed = await Shell.ConfirmAsync(
            "Remove every dangling image on this daemon?", "Prune dangling images")
            .ConfigureAwait(true);
        if (!confirmed) { return; }

        await RunAsync(() => _docker.PruneImagesAsync(dangling: true)).ConfigureAwait(true);
    }

    private async Task ScanAsync()
    {
        BeginTool("Scanning " + _selectedReference + " with Trivy — this pulls a scanner image "
            + "the first time and can take a minute.");
        var reference = _selectedReference;
        await RunAsync(async () =>
        {
            var report = await _docker.ScanImageAsync(reference).ConfigureAwait(true);
            AppendOutput(Formatting.Number(report.Total) + " findings.");
            foreach (var pair in report.CountBySeverity)
            {
                AppendOutput("  " + pair.Key + ": "
                    + pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            Vulnerabilities.Clear();
            var shown = 0;
            foreach (var vulnerability in report.Vulnerabilities)
            {
                if (shown++ >= 100) { break; }
                Vulnerabilities.Add(new VulnerabilityRowViewModel(vulnerability));
            }
            NotifyPropertyChanged(nameof(VulnerabilitiesVisibility));
        }, refreshAfter: false).ConfigureAwait(true);
    }

    private async Task EfficiencyAsync()
    {
        BeginTool("Measuring " + _selectedReference + " with Dive.");
        var reference = _selectedReference;
        await RunAsync(async () =>
        {
            var report = await _docker.AnalyzeImageEfficiencyAsync(reference).ConfigureAwait(true);
            AppendOutput("Efficiency score "
                + report.EfficiencyScore.ToString("F3", CultureInfo.InvariantCulture));
            AppendOutput("Wasted " + Formatting.Bytes(report.WastedBytes) + " of "
                + Formatting.Bytes(report.TotalSizeBytes) + "  ("
                + report.WastedPercent.ToString("F1", CultureInfo.InvariantCulture) + "%)");
            foreach (var layer in report.Layers)
            {
                AppendOutput("  " + Formatting.Bytes(layer.SizeBytes) + "  "
                    + Formatting.Trim(Formatting.OrDash(layer.Command), 110));
            }
        }, refreshAfter: false).ConfigureAwait(true);
    }

    private async Task LintAsync()
    {
        BeginTool("Linting " + DockerfilePath + " with Hadolint.");
        var path = DockerfilePath.Trim();
        await RunAsync(async () =>
        {
            var report = await _docker.LintDockerfileAsync(path).ConfigureAwait(true);
            AppendOutput(Formatting.Number(report.Total) + " findings.");
            foreach (var finding in report.Findings)
            {
                AppendOutput("  " + finding.Level + "  " + finding.Code + "  line "
                    + finding.Line.ToString(CultureInfo.InvariantCulture) + "  "
                    + finding.Message);
            }
        }, refreshAfter: false).ConfigureAwait(true);
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var snapshot = State?.Images;
        if (snapshot is null) { return; }

        Rows.Clear();
        foreach (var image in snapshot)
        {
            if (!string.IsNullOrWhiteSpace(SearchText)
                && (image.DisplayName ?? string.Empty)
                    .IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var row = new ImageRowViewModel(image, SelectRow);
            row.IsSelected = string.Equals(image.DisplayName, _selectedReference,
                StringComparison.Ordinal);
            Rows.Add(row);
        }

        CountText = Rows.Count == snapshot.Count
            ? Formatting.Plural(snapshot.Count, "image")
            : Rows.Count.ToString() + " of " + Formatting.Plural(snapshot.Count, "image");
    }

    private void SelectRow(ImageRowViewModel row)
    {
        if (row is null) { return; }

        foreach (var candidate in Rows)
        {
            candidate.IsSelected = ReferenceEquals(candidate, row);
        }
        _selectedReference = row.DisplayName;
        Title = row.DisplayName;
        Subtitle = row.SizeText + "   ·   " + row.CreatedText + "   ·   " + row.ShortId;
        NotifyPropertyChanged(nameof(HasSelectionVisibility));
        NotifyPropertyChanged(nameof(NoSelectionVisibility));
        _ = LoadDetailAsync(row.Info);
    }

    private async Task LoadDetailAsync(ImageInfo image)
    {
        OverviewFacts.Clear();
        Layers.Clear();
        try
        {
            var detail = await _docker.InspectImageAsync(image.DisplayName).ConfigureAwait(true);
            OverviewFacts.Add(new FactRowViewModel("Id", detail.Id, true));
            OverviewFacts.Add(new FactRowViewModel("Tags", Formatting.Join(detail.RepoTags)));
            OverviewFacts.Add(new FactRowViewModel("Digests",
                Formatting.Trim(Formatting.Join(detail.RepoDigests), 120), true));
            OverviewFacts.Add(new FactRowViewModel("Architecture",
                Formatting.OrDash(detail.Architecture) + " / " + Formatting.OrDash(detail.Os)));
            OverviewFacts.Add(new FactRowViewModel("Size", Formatting.Bytes(detail.SizeBytes)));
            OverviewFacts.Add(new FactRowViewModel("Layers",
                detail.LayerCount.ToString(CultureInfo.InvariantCulture)));
            OverviewFacts.Add(new FactRowViewModel("Created",
                Formatting.Relative(detail.Created)));
            OverviewFacts.Add(new FactRowViewModel("Author", Formatting.OrDash(detail.Author)));
            OverviewFacts.Add(new FactRowViewModel("Entrypoint",
                Formatting.Join(detail.Entrypoint), true));
            OverviewFacts.Add(new FactRowViewModel("Command", Formatting.Join(detail.Cmd), true));
            OverviewFacts.Add(new FactRowViewModel("Working directory",
                Formatting.OrDash(detail.WorkingDir), true));
            OverviewFacts.Add(new FactRowViewModel("User", Formatting.OrDash(detail.User)));
            foreach (var variable in detail.Env)
            {
                OverviewFacts.Add(new FactRowViewModel("env", variable, true));
            }

            var history = await _docker.GetImageHistoryAsync(image.DisplayName)
                .ConfigureAwait(true);
            foreach (var layer in history)
            {
                Layers.Add(new FactRowViewModel(Formatting.Bytes(layer.SizeBytes),
                    Formatting.Trim(Formatting.OrDash(layer.CreatedBy), 160), true));
            }
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private void BeginTool(string heading)
    {
        ToolHeading = heading;
        ToolOutput.Clear();
        Vulnerabilities.Clear();
        NotifyPropertyChanged(nameof(ToolOutputVisibility));
        NotifyPropertyChanged(nameof(VulnerabilitiesVisibility));
        AppendOutput("started " + Formatting.Clock(DateTimeOffset.Now));
    }

    private void AppendOutput(string line)
    {
        if (string.IsNullOrEmpty(line)) { return; }

        ToolOutput.Add(line);
        while (ToolOutput.Count > 400)
        {
            ToolOutput.RemoveAt(0);
        }
        NotifyPropertyChanged(nameof(ToolOutputVisibility));
    }
}
