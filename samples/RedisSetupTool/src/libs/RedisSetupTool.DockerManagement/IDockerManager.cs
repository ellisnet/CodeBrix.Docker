using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Exec;
using RedisSetupTool.DockerManagement.Models;

namespace RedisSetupTool.DockerManagement;

/// <summary>
/// The single Docker facade. Everything the application does to the daemon goes through here, and
/// nothing on this contract names a CodeBrix.Docker type - that is the whole point of the library.
/// </summary>
public interface IDockerManager : IDisposable
{
    /// <summary>Gets the daemon endpoint in use.</summary>
    string Endpoint { get; }

    /// <summary>Gets the advisor rule ids that can appear in a finding.</summary>
    IReadOnlyList<string> AdvisorRuleIds { get; }

    /// <summary>Asks the daemon whether it is there.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True when the daemon answered.</returns>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the daemon's version and system information in one call.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What the daemon reports about itself.</returns>
    Task<DaemonInfo> GetDaemonInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads what images, containers, volumes and the build cache are costing.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The disk usage.</returns>
    Task<DaemonDiskUsage> GetDiskUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams the daemon's events until the token is cancelled.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The event stream.</returns>
    IAsyncEnumerable<DaemonEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists containers.</summary>
    /// <param name="includeStopped">Whether containers that are not running are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The containers.</returns>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool includeStopped,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every container this tool created, in any state.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The containers.</returns>
    Task<IReadOnlyList<ContainerInfo>> ListManagedContainersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Lists one instance's containers, in any state.</summary>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The containers.</returns>
    Task<IReadOnlyList<ContainerInfo>> ListInstanceContainersAsync(string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Inspects one container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The detail.</returns>
    Task<ContainerDetail> InspectContainerAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon accepts the start.</returns>
    Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Stops a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeoutSeconds">How long to wait before killing it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container has stopped.</returns>
    Task StopContainerAsync(string idOrName, int timeoutSeconds = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Restarts a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeoutSeconds">How long to wait before killing it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is running again.</returns>
    Task RestartContainerAsync(string idOrName, int timeoutSeconds = 10,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a signal to a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="signal">The signal name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the signal is delivered.</returns>
    Task KillContainerAsync(string idOrName, string signal = "SIGKILL",
        CancellationToken cancellationToken = default);

    /// <summary>Removes a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="force">Whether a running container is removed anyway.</param>
    /// <param name="removeVolumes">Whether anonymous volumes go with it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is gone.</returns>
    Task RemoveContainerAsync(string idOrName, bool force = false, bool removeVolumes = false,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every stopped container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the prune finishes.</returns>
    Task PruneContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes a running container's resource limits.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="limits">The limits to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the daemon applies the limits.</returns>
    Task UpdateResourcesAsync(string idOrName, ResourceLimitUpdate limits,
        CancellationToken cancellationToken = default);

    /// <summary>Waits for a container to exit.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The exit code.</returns>
    Task<long> WaitForExitAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Reads a container's captured output.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="tail">How many lines from the end; null means all of them.</param>
    /// <param name="timestamps">Whether each line is prefixed with its timestamp.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The captured text.</returns>
    /// <remarks>There is no follow API: a live view polls this.</remarks>
    Task<ContainerLogText> GetLogsAsync(string idOrName, int? tail = 500, bool timestamps = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one sample of a container's resource use.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The sample.</returns>
    Task<ContainerStatsSample> GetStatsAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Streams samples of a container's resource use until the token is cancelled.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The sample stream.</returns>
    IAsyncEnumerable<ContainerStatsSample> StreamStatsAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Runs one command inside a container and buffers its output.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="user">The user to run as.</param>
    /// <param name="workingDir">The working directory.</param>
    /// <param name="env">Extra environment variables.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The output and exit code.</returns>
    Task<CommandResult> RunCommandAsync(string idOrName, IReadOnlyList<string> command,
        string user = null, string workingDir = null, IReadOnlyList<string> env = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists locally stored images.</summary>
    /// <param name="all">Whether intermediate layers are included.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The images.</returns>
    Task<IReadOnlyList<ImageInfo>> ListImagesAsync(bool all = false,
        CancellationToken cancellationToken = default);

    /// <summary>Inspects one image.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The detail.</returns>
    Task<ImageDetail> InspectImageAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Reads an image's build history.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The layers, newest first.</returns>
    Task<IReadOnlyList<ImageLayerInfo>> GetImageHistoryAsync(string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Pulls an image.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="progress">Receives the daemon's progress lines.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the image is local.</returns>
    Task PullImageAsync(string reference, IProgress<string> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an image or one of its tags.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="force">Whether the image goes even when tagged or used.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the image is gone.</returns>
    Task RemoveImageAsync(string reference, bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a tag to an image.</summary>
    /// <param name="sourceReference">The existing reference.</param>
    /// <param name="targetReference">The new reference.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the tag exists.</returns>
    Task TagImageAsync(string sourceReference, string targetReference,
        CancellationToken cancellationToken = default);

    /// <summary>Removes unused images.</summary>
    /// <param name="dangling">Whether only untagged images go.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the prune finishes.</returns>
    Task PruneImagesAsync(bool dangling = true, CancellationToken cancellationToken = default);

    /// <summary>Builds an image.</summary>
    /// <param name="request">What to build.</param>
    /// <param name="progress">Receives the builder's output lines.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ImageBuildOutcome> BuildImageAsync(ImageBuildRequest request,
        IProgress<string> progress = null, CancellationToken cancellationToken = default);

    /// <summary>Lists networks.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The networks.</returns>
    Task<IReadOnlyList<NetworkInfo>> ListNetworksAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspects one network, including its gateway and attachments.</summary>
    /// <param name="idOrName">The network id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The network.</returns>
    Task<NetworkInfo> InspectNetworkAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Creates a bridge network.</summary>
    /// <param name="name">The network name.</param>
    /// <param name="labels">Labels to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The new network's id.</returns>
    Task<string> CreateNetworkAsync(string name, IReadOnlyDictionary<string, string> labels = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a network.</summary>
    /// <param name="idOrName">The network id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the network is gone.</returns>
    Task RemoveNetworkAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Attaches a container to a network.</summary>
    /// <param name="network">The network id or name.</param>
    /// <param name="container">The container id or name.</param>
    /// <param name="aliases">Network aliases for the container.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is attached.</returns>
    Task ConnectContainerAsync(string network, string container,
        IReadOnlyList<string> aliases = null, CancellationToken cancellationToken = default);

    /// <summary>Detaches a container from a network.</summary>
    /// <param name="network">The network id or name.</param>
    /// <param name="container">The container id or name.</param>
    /// <param name="force">Whether the detach happens even if the container objects.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is detached.</returns>
    Task DisconnectContainerAsync(string network, string container, bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Removes unused networks.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the prune finishes.</returns>
    Task PruneNetworksAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists volumes.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volumes.</returns>
    Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspects one volume.</summary>
    /// <param name="name">The volume name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volume.</returns>
    Task<VolumeInfo> InspectVolumeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a volume.</summary>
    /// <param name="name">The volume name; null asks the daemon to invent one.</param>
    /// <param name="labels">Labels to apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The volume's name.</returns>
    Task<string> CreateVolumeAsync(string name, IReadOnlyDictionary<string, string> labels = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a volume.</summary>
    /// <param name="name">The volume name.</param>
    /// <param name="force">Whether the volume goes even when the daemon objects.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the volume is gone.</returns>
    Task RemoveVolumeAsync(string name, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Removes unused volumes.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the prune finishes.</returns>
    Task PruneVolumesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs the whole diagnostics tier over one container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<DiagnosticsReport> DiagnoseAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Reads how much CPU time the kernel took away from a container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<CpuThrottlingInfo> GetCpuThrottlingAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Reads where a container's memory went.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<MemoryBreakdownInfo> GetMemoryBreakdownAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Reads whether a container met the out-of-memory killer.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<OomInfo> CheckOomAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Reads a container's healthcheck state.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<HealthInfo> GetHealthAsync(string idOrName, CancellationToken cancellationToken = default);

    /// <summary>Waits until a container's healthcheck passes.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the container is healthy.</returns>
    Task WaitForHealthyAsync(string idOrName, TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the advisor over one container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The findings.</returns>
    Task<IReadOnlyList<AdvisorFindingInfo>> AdviseContainerAsync(string idOrName,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the advisor over every container.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The findings.</returns>
    Task<IReadOnlyList<AdvisorFindingInfo>> AdviseAllContainersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Scans an image for known vulnerabilities.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="options">How to scan.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImageScanReport> ScanImageAsync(string reference, ImageScanOptions options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Measures how much of an image is wasted bytes.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImageEfficiencyReport> AnalyzeImageEfficiencyAsync(string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Lints a Dockerfile.</summary>
    /// <param name="dockerfilePath">The file to lint.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<DockerfileLintReport> LintDockerfileAsync(string dockerfilePath,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a smaller version of an image.</summary>
    /// <param name="reference">The image reference.</param>
    /// <param name="options">How to optimize.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The report.</returns>
    Task<ImageOptimizeReport> OptimizeImageAsync(string reference, ImageOptimizeOptions options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Finds out which shell a container has.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="candidates">The shells to try; null uses the default list.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The probe result.</returns>
    Task<ShellProbeResult> ProbeShellAsync(string idOrName, IReadOnlyList<string> candidates = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens an interactive shell inside a running container.</summary>
    /// <param name="idOrName">The container id or name.</param>
    /// <param name="options">How to open it.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The live session.</returns>
    Task<IExecSession> OpenShellAsync(string idOrName, ExecSessionOptions options = null,
        CancellationToken cancellationToken = default);
}
