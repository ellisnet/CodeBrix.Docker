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

/// <summary>One row of the network list.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class NetworkRowViewModel : SimpleViewModel
{
    private readonly Action<NetworkRowViewModel> _select;
    private bool _isSelected;

    /// <summary>Wraps one network.</summary>
    /// <param name="network">The network to show.</param>
    /// <param name="select">What selecting the row does.</param>
    public NetworkRowViewModel(NetworkInfo network, Action<NetworkRowViewModel> select)
    {
        Info = network;
        _select = select;
        Name = network.Name;
        Detail = Formatting.OrDash(network.Driver) + " · " + Formatting.OrDash(network.Scope)
            + " · " + Formatting.OrDash(network.Subnet) + " · gateway "
            + Formatting.OrDash(network.Gateway);
        AttachedText = Formatting.Plural(network.AttachedContainerCount, "container");
        IsManaged = !string.IsNullOrEmpty(network.InstanceId);
        IsPredefined = network.IsPredefined;
    }

    /// <summary>The network this row shows.</summary>
    public NetworkInfo Info { get; }

    /// <summary>The network's name.</summary>
    public string Name { get; }

    /// <summary>Driver, scope, subnet and gateway on one line.</summary>
    public string Detail { get; }

    /// <summary>How many containers are attached.</summary>
    public string AttachedText { get; }

    /// <summary>Whether this tool created the network.</summary>
    public bool IsManaged { get; }

    /// <summary>Whether the instance chip is showing.</summary>
    public Visibility ManagedVisibility => GetVisibility(IsManaged);

    /// <summary>Whether Docker itself owns the network, so it cannot be removed.</summary>
    public bool IsPredefined { get; }

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

/// <summary>One row of the volume list.</summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class VolumeRowViewModel : SimpleViewModel
{
    private readonly Action<VolumeRowViewModel> _select;
    private bool _isSelected;

    /// <summary>Wraps one volume.</summary>
    /// <param name="volume">The volume to show.</param>
    /// <param name="select">What selecting the row does.</param>
    public VolumeRowViewModel(VolumeInfo volume, Action<VolumeRowViewModel> select)
    {
        Info = volume;
        _select = select;
        Name = volume.Name;
        Detail = Formatting.OrDash(volume.Driver) + " · " + Formatting.OrDash(volume.Mountpoint);
        SizeText = volume.SizeBytes.HasValue ? Formatting.Bytes(volume.SizeBytes.Value) : "—";
        RefCountText = volume.RefCount.HasValue
            ? Formatting.Plural((int)volume.RefCount.Value, "use")
            : "—";
        CreatedText = Formatting.Relative(volume.CreatedAt);
        IsManaged = !string.IsNullOrEmpty(volume.InstanceId);
    }

    /// <summary>The volume this row shows.</summary>
    public VolumeInfo Info { get; }

    /// <summary>The volume's name.</summary>
    public string Name { get; }

    /// <summary>Driver and mountpoint on one line.</summary>
    public string Detail { get; }

    /// <summary>The volume's size, when the daemon reported one.</summary>
    public string SizeText { get; }

    /// <summary>How many containers reference the volume.</summary>
    public string RefCountText { get; }

    /// <summary>How long ago the volume was created.</summary>
    public string CreatedText { get; }

    /// <summary>Whether this tool created the volume.</summary>
    public bool IsManaged { get; }

    /// <summary>Whether the instance chip is showing.</summary>
    public Visibility ManagedVisibility => GetVisibility(IsManaged);

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

/// <summary>
/// Section 7 — networks above, volumes below. Each half is a list with a small detail block and
/// the create, remove and prune actions; removing a volume that a live instance is still using
/// warns first.
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class NetworksVolumesViewModel : SectionViewModel
{
    private readonly IDockerManager _docker;
    private string _selectedNetwork;
    private string _selectedVolume;

    /// <summary>Creates the networks-and-volumes section.</summary>
    /// <param name="shell">The shell this section belongs to.</param>
    public NetworksVolumesViewModel(IShellContext shell)
        : base(shell)
    {
        _docker = GetService<IDockerManager>();
    }

    #region | Bindable properties |

    /// <summary>Every network on the daemon.</summary>
    public ObservableCollection<NetworkRowViewModel> Networks { get; } = [];

    /// <summary>Every volume on the daemon.</summary>
    public ObservableCollection<VolumeRowViewModel> Volumes { get; } = [];

    /// <summary>The selected network's attachments, one fact row each.</summary>
    public ObservableCollection<FactRowViewModel> NetworkDetail { get; } = [];

    /// <summary>The selected volume's facts.</summary>
    public ObservableCollection<FactRowViewModel> VolumeDetail { get; } = [];

    /// <summary>The selected network's name, as a heading.</summary>
    public string NetworkTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "No network selected";

    /// <summary>The selected volume's name, as a heading.</summary>
    public string VolumeTitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "No volume selected";

    /// <summary>The name the create-network button uses.</summary>
    [AffectsCommands(nameof(CreateNetworkCommand))]
    public string NewNetworkName
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>The name the create-volume button uses.</summary>
    [AffectsCommands(nameof(CreateVolumeCommand))]
    public string NewVolumeName
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>How many networks and volumes there are, as a caption.</summary>
    public string CountText
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    #endregion

    #region | Commands and their implementations |

    /// <summary>Re-reads everything from the daemon.</summary>
    public SimpleCommand RefreshCommand => field ??=
        new SimpleCommand((Func<Task>)(() => Shell.RefreshAsync()));

    /// <summary>Creates a network with the typed name.</summary>
    public SimpleCommand CreateNetworkCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrWhiteSpace(NewNetworkName),
        (Func<Task>)(async () =>
        {
            var name = NewNetworkName.Trim();
            if (await RunAsync(() => _docker.CreateNetworkAsync(name)).ConfigureAwait(true))
            {
                NewNetworkName = string.Empty;
            }
        }));

    /// <summary>Removes the selected network, after confirming.</summary>
    public SimpleCommand RemoveNetworkCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrEmpty(_selectedNetwork), (Func<Task>)RemoveNetworkAsync);

    /// <summary>Removes every unused network, after confirming.</summary>
    public SimpleCommand PruneNetworksCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(async () =>
        {
            if (await Shell.ConfirmAsync("Remove every unused network on this daemon?",
                "Prune networks").ConfigureAwait(true))
            {
                await RunAsync(() => _docker.PruneNetworksAsync()).ConfigureAwait(true);
            }
        }));

    /// <summary>Creates a volume with the typed name.</summary>
    public SimpleCommand CreateVolumeCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrWhiteSpace(NewVolumeName),
        (Func<Task>)(async () =>
        {
            var name = NewVolumeName.Trim();
            if (await RunAsync(() => _docker.CreateVolumeAsync(name)).ConfigureAwait(true))
            {
                NewVolumeName = string.Empty;
            }
        }));

    /// <summary>Removes the selected volume, after confirming.</summary>
    public SimpleCommand RemoveVolumeCommand => field ??= new SimpleCommand(
        () => !IsBusy && !string.IsNullOrEmpty(_selectedVolume), (Func<Task>)RemoveVolumeAsync);

    /// <summary>Removes every unused volume, after confirming.</summary>
    public SimpleCommand PruneVolumesCommand => field ??= new SimpleCommand(
        () => !IsBusy, (Func<Task>)(async () =>
        {
            if (await Shell.ConfirmAsync(
                "Remove every unused volume on this daemon? Their contents go with them.",
                "Prune volumes").ConfigureAwait(true))
            {
                await RunAsync(() => _docker.PruneVolumesAsync()).ConfigureAwait(true);
            }
        }));

    private async Task RemoveNetworkAsync()
    {
        var name = _selectedNetwork;
        var warning = IsInstanceResource(name)
            ? "\n\nThis network belongs to a Redis instance this tool created. Destroy the "
                + "instance instead if you want it cleaned up properly."
            : string.Empty;

        if (await Shell.ConfirmAsync("Remove the network " + name + "?" + warning,
            "Remove network").ConfigureAwait(true))
        {
            await RunAsync(() => _docker.RemoveNetworkAsync(name)).ConfigureAwait(true);
        }
    }

    private async Task RemoveVolumeAsync()
    {
        var name = _selectedVolume;
        var warning = IsInstanceVolume(name)
            ? "\n\nThis volume belongs to a Redis instance this tool created, and holds its data. "
                + "Destroy the instance instead if you want it cleaned up properly."
            : string.Empty;

        if (await Shell.ConfirmAsync("Remove the volume " + name + "? Its contents go with it."
            + warning, "Remove volume").ConfigureAwait(true))
        {
            await RunAsync(() => _docker.RemoveVolumeAsync(name, force: true)).ConfigureAwait(true);
        }
    }

    #endregion

    /// <inheritdoc />
    public override void ApplySnapshot()
    {
        var state = State;
        if (state is null) { return; }

        Networks.Clear();
        foreach (var network in state.Networks)
        {
            var row = new NetworkRowViewModel(network, SelectNetwork);
            row.IsSelected = string.Equals(network.Name, _selectedNetwork, StringComparison.Ordinal);
            Networks.Add(row);
        }

        Volumes.Clear();
        foreach (var volume in state.Volumes)
        {
            var row = new VolumeRowViewModel(volume, SelectVolume);
            row.IsSelected = string.Equals(volume.Name, _selectedVolume, StringComparison.Ordinal);
            Volumes.Add(row);
        }

        CountText = Formatting.Plural(Networks.Count, "network") + "   ·   "
            + Formatting.Plural(Volumes.Count, "volume");
    }

    private void SelectNetwork(NetworkRowViewModel row)
    {
        if (row is null) { return; }

        foreach (var candidate in Networks)
        {
            candidate.IsSelected = ReferenceEquals(candidate, row);
        }
        _selectedNetwork = row.Name;
        NetworkTitle = row.Name;

        NetworkDetail.Clear();
        var network = row.Info;
        NetworkDetail.Add(new FactRowViewModel("Id", Formatting.OrDash(network.Id), true));
        NetworkDetail.Add(new FactRowViewModel("Driver", Formatting.OrDash(network.Driver)));
        NetworkDetail.Add(new FactRowViewModel("Scope", Formatting.OrDash(network.Scope)));
        NetworkDetail.Add(new FactRowViewModel("Subnet", Formatting.OrDash(network.Subnet), true));
        NetworkDetail.Add(new FactRowViewModel("Gateway", Formatting.OrDash(network.Gateway), true));
        NetworkDetail.Add(new FactRowViewModel("Internal", network.IsInternal ? "yes" : "no"));
        NetworkDetail.Add(new FactRowViewModel("Attachable", network.IsAttachable ? "yes" : "no"));
        NetworkDetail.Add(new FactRowViewModel("Created", Formatting.Relative(network.Created)));
        if (!string.IsNullOrEmpty(network.InstanceId))
        {
            NetworkDetail.Add(new FactRowViewModel("Instance", network.InstanceId, true));
        }
        foreach (var attachment in network.AttachedContainers)
        {
            NetworkDetail.Add(new FactRowViewModel(attachment.ContainerName,
                Formatting.OrDash(attachment.IPv4Address) + "   "
                + Formatting.OrDash(attachment.MacAddress), true));
        }
    }

    private void SelectVolume(VolumeRowViewModel row)
    {
        if (row is null) { return; }

        foreach (var candidate in Volumes)
        {
            candidate.IsSelected = ReferenceEquals(candidate, row);
        }
        _selectedVolume = row.Name;
        VolumeTitle = row.Name;

        VolumeDetail.Clear();
        var volume = row.Info;
        VolumeDetail.Add(new FactRowViewModel("Driver", Formatting.OrDash(volume.Driver)));
        VolumeDetail.Add(new FactRowViewModel("Mountpoint",
            Formatting.OrDash(volume.Mountpoint), true));
        VolumeDetail.Add(new FactRowViewModel("Scope", Formatting.OrDash(volume.Scope)));
        VolumeDetail.Add(new FactRowViewModel("Created", Formatting.Relative(volume.CreatedAt)));
        VolumeDetail.Add(new FactRowViewModel("Size", row.SizeText));
        VolumeDetail.Add(new FactRowViewModel("References", row.RefCountText));
        if (!string.IsNullOrEmpty(volume.InstanceId))
        {
            VolumeDetail.Add(new FactRowViewModel("Instance", volume.InstanceId, true));
        }
        foreach (var label in volume.Labels)
        {
            VolumeDetail.Add(new FactRowViewModel(label.Key, label.Value, true));
        }
    }

    private bool IsInstanceResource(string networkName)
    {
        foreach (var network in Networks)
        {
            if (string.Equals(network.Name, networkName, StringComparison.Ordinal))
            {
                return network.IsManaged;
            }
        }
        return false;
    }

    private bool IsInstanceVolume(string volumeName)
    {
        foreach (var volume in Volumes)
        {
            if (string.Equals(volume.Name, volumeName, StringComparison.Ordinal))
            {
                return volume.IsManaged;
            }
        }
        return false;
    }
}
