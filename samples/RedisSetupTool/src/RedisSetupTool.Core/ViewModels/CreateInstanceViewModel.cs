using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;
using RedisSetupTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace RedisSetupTool.ViewModels;

/// <summary>
/// Section 3 — the create form. The catalog on the left, and on the right the chosen topology's
/// explanation, a form generated from its parameters, the host ports it would take, whatever is
/// still wrong with the request, and the create strip with live progress.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class CreateInstanceViewModel : SectionViewModel
{
    private readonly IRedisTopologyService _topologies;
    private CancellationTokenSource _createCancellation;
    private bool _isBuildingForm;

    /// <summary>Creates the create-instance section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public CreateInstanceViewModel(IShellContext shell)
        : base(shell)
    {
        _topologies = GetService<IRedisTopologyService>();
        BuildCatalog();
        SelectFirst();
    }

    #region | Bindable properties |

    /// <summary>The catalog, grouped by category.</summary>
    public ObservableCollection<TopologyGroupViewModel> Groups { get; } = [];

    /// <summary>The chosen topology's row, or null before anything is chosen.</summary>
    public TopologyChoiceViewModel SelectedTopology
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The chosen topology's code, for the chip beside the title.</summary>
    public string SelectedCode
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The chosen topology's display name.</summary>
    public string SelectedTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Choose a topology";

    /// <summary>The chosen topology's full explanation.</summary>
    public string SelectedDetail
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Pick a preset on the left to see what it builds and what it needs.";

    /// <summary>The image the chosen topology runs, and how many containers it takes.</summary>
    public string SelectedOrigin
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>The name the new instance will carry.</summary>
    [AffectsCommands(nameof(CreateCommand))]
    public string InstanceName
    {
        get;
        set
        {
            SetProperty(ref field, value ?? string.Empty);
            Revalidate();
        }
    } = string.Empty;

    /// <summary>The generated parameter fields for the chosen topology.</summary>
    public ObservableCollection<ParameterFieldViewModel> Fields { get; } = [];

    /// <summary>Whether the chosen topology takes any parameters at all.</summary>
    public Visibility FieldsVisibility => GetVisibility(Fields.Count > 0);

    /// <summary>The host ports the instance would take, as a monospace line.</summary>
    public string PortPlanText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "—";

    /// <summary>Everything still wrong with the request.</summary>
    public ObservableCollection<string> ValidationMessages { get; } = [];

    /// <summary>Whether the validation block is showing.</summary>
    public Visibility ValidationVisibility => GetVisibility(ValidationMessages.Count > 0);

    /// <summary>Whether the request is complete enough to create.</summary>
    [AffectsCommands(nameof(CreateCommand))]
    public bool CanCreate
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Whether a create is in flight.</summary>
    [AffectsAllCommands]
    public bool IsCreating
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(ProgressVisibility));
            NotifyPropertyChanged(nameof(FormEnabled));
        }
    }

    /// <summary>Whether the form accepts edits, which it does not while a create runs.</summary>
    public bool FormEnabled => !IsCreating;

    /// <summary>Whether the progress block is showing.</summary>
    public Visibility ProgressVisibility => GetVisibility(IsCreating || ProgressLines.Count > 0);

    /// <summary>The progress lines the topology service reported, newest last.</summary>
    public ObservableCollection<string> ProgressLines { get; } = [];

    /// <summary>The most recent progress line, shown large above the list.</summary>
    public string ProgressHeadline
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    #endregion

    #region | Commands and their implementations |

    /// <summary>Builds the instance.</summary>
    public SimpleCommand CreateCommand => field ??=
        new SimpleCommand(() => CanCreate && !IsCreating, (Func<Task>)CreateAsync);

    /// <summary>Abandons the form and goes back to the instance list.</summary>
    public SimpleCommand CancelCommand => field ??= new SimpleCommand(Cancel);

    private void Cancel()
    {
        if (IsCreating)
        {
            _createCancellation?.Cancel();
            return;
        }
        Shell?.Navigate(SectionKey.Instances);
    }

    private async Task CreateAsync()
    {
        var descriptor = SelectedTopology?.Descriptor;
        if (descriptor is null) { return; }

        var request = BuildRequest(descriptor);
        var parameters = new Dictionary<string, string>(request.Parameters, StringComparer.Ordinal);

        IsCreating = true;
        SetError(null);
        ProgressLines.Clear();
        ProgressHeadline = "Starting…";
        Shell?.PauseAutoRefresh();
        _createCancellation = new CancellationTokenSource();

        var progress = new Progress<TopologyProgress>(report => InvokeOnMainThread(() =>
        {
            var line = report.Step.ToString(CultureInfo.InvariantCulture) + "/"
                + report.TotalSteps.ToString(CultureInfo.InvariantCulture) + "  " + report.Message;
            ProgressLines.Add(line);
            ProgressHeadline = report.Message;
            Shell?.LogAutomation("create progress: " + line);
        }));

        try
        {
            var instance = await _topologies
                .CreateAsync(request, progress, _createCancellation.Token).ConfigureAwait(true);

            Shell?.LogAutomation("create " + descriptor.Code + ": OK id=" + instance.InstanceId
                + " name=" + instance.InstanceName);
            ProgressHeadline = instance.InstanceName + " is ready.";

            await Shell.RefreshAsync().ConfigureAwait(true);
            NoteCreated(instance.InstanceId, parameters);
            Shell?.Navigate(SectionKey.Instances);
        }
        catch (OperationCanceledException)
        {
            ProgressHeadline = "Cancelled.";
            Shell?.LogAutomation("create " + descriptor.Code + ": CANCELLED");
        }
        catch (Exception exception)
        {
            ProgressHeadline = "Failed.";
            SetError(exception.Message);
            Shell?.LogAutomation("create " + descriptor.Code + ": ERROR - " + exception.Message);
        }
        finally
        {
            _createCancellation?.Dispose();
            _createCancellation = null;
            IsCreating = false;
            Shell?.ResumeAutoRefresh();
        }
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        //The form does not read the snapshot; the port preview is refreshed when the topology
        //  or the daemon's port usage might have moved.
        if (!IsCreating)
        {
            _ = RefreshPortPlanAsync();
        }
    }

    /// <summary>
    /// Chooses a topology by its code. The automation hook and the "create one like this"
    /// affordances use it; the catalog rows call the row's own select command.
    /// </summary>
    /// <param name="code">The topology code, for example <c>A1</c>.</param>
    /// <returns>True when the code named a topology in the catalog.</returns>
    public bool SelectByCode(string code)
    {
        foreach (var group in Groups)
        {
            foreach (var choice in group.Items)
            {
                if (string.Equals(choice.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    Select(choice);
                    return true;
                }
            }
        }
        return false;
    }

    private void BuildCatalog()
    {
        var groups = new Dictionary<TopologyCategory, TopologyGroupViewModel>();
        foreach (var descriptor in _topologies.Catalog)
        {
            if (!groups.TryGetValue(descriptor.Category, out var group))
            {
                group = new TopologyGroupViewModel(descriptor.Category);
                groups[descriptor.Category] = group;
                Groups.Add(group);
            }
            group.Items.Add(new TopologyChoiceViewModel(descriptor, Select));
        }
    }

    private void SelectFirst()
    {
        if (Groups.Count > 0 && Groups[0].Items.Count > 0)
        {
            Select(Groups[0].Items[0]);
        }
    }

    private void Select(TopologyChoiceViewModel choice)
    {
        if (choice is null || IsCreating) { return; }

        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.IsSelected = ReferenceEquals(item, choice);
            }
        }

        SelectedTopology = choice;
        var descriptor = choice.Descriptor;
        SelectedCode = descriptor.Code;
        SelectedTitle = descriptor.DisplayName;
        SelectedDetail = descriptor.Detail;
        SelectedOrigin = descriptor.Image + "   ·   " + choice.CountText
            + "   ·   " + descriptor.ConnectionShape.ToString();

        _isBuildingForm = true;
        Fields.Clear();
        foreach (var parameter in descriptor.Parameters)
        {
            Fields.Add(new ParameterFieldViewModel(parameter, Revalidate));
        }
        _isBuildingForm = false;
        NotifyPropertyChanged(nameof(FieldsVisibility));

        InstanceName = descriptor.Code.ToLowerInvariant() + "-" + InstanceId.RandomHex(4);
        ProgressLines.Clear();
        ProgressHeadline = string.Empty;
        NotifyPropertyChanged(nameof(ProgressVisibility));
        SetError(null);

        Revalidate();
        _ = RefreshPortPlanAsync();
    }

    private TopologyRequest BuildRequest(TopologyDescriptor descriptor)
    {
        var request = new TopologyRequest
        {
            TopologyId = descriptor.Id,
            InstanceName = InstanceName?.Trim(),
        };
        foreach (var withField in Fields)
        {
            request.Parameters[withField.Key] = withField.Value;
        }
        return request;
    }

    private void Revalidate()
    {
        if (_isBuildingForm) { return; }

        var descriptor = SelectedTopology?.Descriptor;
        ValidationMessages.Clear();

        if (descriptor is null)
        {
            CanCreate = false;
            NotifyPropertyChanged(nameof(ValidationVisibility));
            return;
        }

        var name = InstanceName?.Trim() ?? string.Empty;
        if (name.Length > 0 && !InstanceId.IsValidResourceName(name))
        {
            ValidationMessages.Add(
                "The name may hold only letters, digits, dots, dashes and underscores, "
                + "must start with a letter or digit, and must be 63 characters or fewer.");
        }

        foreach (var problem in _topologies.Validate(BuildRequest(descriptor)))
        {
            ValidationMessages.Add(problem);
        }

        CanCreate = ValidationMessages.Count == 0;
        NotifyPropertyChanged(nameof(ValidationVisibility));
    }

    private async Task RefreshPortPlanAsync()
    {
        var descriptor = SelectedTopology?.Descriptor;
        if (descriptor is null)
        {
            PortPlanText = "—";
            return;
        }

        try
        {
            var plan = await _topologies
                .PreviewPortsAsync(new TopologyRequest { TopologyId = descriptor.Id })
                .ConfigureAwait(true);
            PortPlanText = plan.Describe();
        }
        catch (Exception exception)
        {
            PortPlanText = "port plan unavailable — " + exception.Message;
        }
    }

    private void NoteCreated(string instanceId, IReadOnlyDictionary<string, string> parameters)
    {
        if (Shell is IInstanceParameterSink sink)
        {
            sink.RememberInstanceParameters(instanceId, parameters);
        }
    }
}

/// <summary>
/// How the create form hands the parameter values it used to whatever keeps them. Only the
/// eviction policy actually needs remembering: nothing else the verification asserts is missing
/// from an instance's labels.
/// </summary>
public interface IInstanceParameterSink
{
    /// <summary>Remembers the values an instance was created with.</summary>
    /// <param name="instanceId">The new instance's id.</param>
    /// <param name="parameters">The parameter values the form used.</param>
    void RememberInstanceParameters(string instanceId,
        IReadOnlyDictionary<string, string> parameters);
}
