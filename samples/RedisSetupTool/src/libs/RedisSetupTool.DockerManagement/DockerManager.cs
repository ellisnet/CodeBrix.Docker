using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using RedisSetupTool.DockerManagement.Exec;
using RedisSetupTool.DockerManagement.Mapping;
using RedisSetupTool.DockerManagement.Models;
using RedisSetupTool.DockerManagement.Instances;

namespace RedisSetupTool.DockerManagement;

/// <summary>
/// The one implementation of <see cref="IDockerManager"/>. It owns the CodeBrix.Docker client, maps
/// every result into this library's DTOs and translates every library exception into
/// <see cref="DockerManagementException"/>.
/// </summary>
public sealed class DockerManager : IDockerManager
{
    private readonly DockerClient _client;
    private int _disposed;

    static DockerManager()
    {
        //The CodeBrix.Docker reference is private, so its assembly is not in any consumer's
        //  deps.json; this hooks the resolver before the first type in it is touched.
        DockerAssemblyResolver.EnsureRegistered();
    }

    /// <summary>Creates the facade.</summary>
    /// <param name="options">Endpoint and timeout options; null selects the defaults.</param>
    public DockerManager(DockerManagerOptions options = null)
    {
        var settings = options ?? new DockerManagerOptions();
        _client = DockerClient.Create(new DockerClientOptions
        {
            Endpoint = settings.Endpoint,
            DockerCliPath = settings.DockerCliPath,
            DefaultTimeout = settings.DefaultTimeout,
        });
    }

    /// <inheritdoc />
    public string Endpoint => _client.Endpoint;

    /// <inheritdoc />
    public IReadOnlyList<string> AdvisorRuleIds => AdvisorEngine.RuleIds;

    internal DockerClient Client => _client;

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.System.PingAsync(cancellationToken));

    /// <inheritdoc />
    public Task<DaemonInfo> GetDaemonInfoAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var reachable = await _client.System.PingAsync(cancellationToken).ConfigureAwait(false);
            if (!reachable)
            {
                return SystemMapper.ToInfo(false, _client.Endpoint, null, null);
            }

            var version = await _client.System.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            var info = await _client.System.GetInfoAsync(cancellationToken).ConfigureAwait(false);
            return SystemMapper.ToInfo(true, _client.Endpoint, version, info);
        });

    /// <inheritdoc />
    public Task<DaemonDiskUsage> GetDiskUsageAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
            SystemMapper.ToUsage(await _client.System.GetDiskUsageAsync(cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public async IAsyncEnumerable<DaemonEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var source in _client.System.StreamEventsAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return SystemMapper.ToEvent(source);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool includeStopped,
        CancellationToken cancellationToken = default) =>
        ListContainersAsync(labelFilters: null, includeStopped, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListManagedContainersAsync(
        CancellationToken cancellationToken = default) =>
        ListContainersAsync(InstanceLabels.PresenceFilter, includeStopped: true, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListInstanceContainersAsync(string instanceId,
        CancellationToken cancellationToken = default) =>
        ListContainersAsync(InstanceLabels.InstanceFilter(instanceId), includeStopped: true,
            cancellationToken);

    /// <inheritdoc />
    public Task<ContainerDetail> InspectContainerAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => ContainerMapper.ToDetail(
            await _client.Containers.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task StartContainerAsync(string idOrName, CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.StartAsync(idOrName, cancellationToken));

    /// <inheritdoc />
    public Task StopContainerAsync(string idOrName, int timeoutSeconds = 10,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.StopAsync(idOrName, timeoutSeconds, cancellationToken));

    /// <inheritdoc />
    public Task RestartContainerAsync(string idOrName, int timeoutSeconds = 10,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.RestartAsync(idOrName, timeoutSeconds, cancellationToken));

    /// <inheritdoc />
    public Task KillContainerAsync(string idOrName, string signal = "SIGKILL",
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.KillAsync(idOrName, signal, cancellationToken));

    /// <inheritdoc />
    public Task RemoveContainerAsync(string idOrName, bool force = false, bool removeVolumes = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.RemoveAsync(idOrName, force, removeVolumes, cancellationToken));

    /// <inheritdoc />
    public Task PruneContainersAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.PruneAsync(labelFilters: null, cancellationToken));

    /// <inheritdoc />
    public Task UpdateResourcesAsync(string idOrName, ResourceLimitUpdate limits,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.UpdateResourcesAsync(idOrName,
            ContainerMapper.ToResourceLimits(limits), cancellationToken));

    /// <inheritdoc />
    public Task<long> WaitForExitAsync(string idOrName, CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Containers.WaitForExitAsync(idOrName, cancellationToken));

    /// <inheritdoc />
    public Task<ContainerLogText> GetLogsAsync(string idOrName, int? tail = 500,
        bool timestamps = false, CancellationToken cancellationToken = default) =>
        RunAsync(async () => ContainerMapper.ToLogText(
            await _client.Containers.GetLogsAsync(idOrName, tail, timestamps, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<ContainerStatsSample> GetStatsAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => ContainerMapper.ToStatsSample(
            await _client.Containers.GetStatsAsync(idOrName, cancellationToken).ConfigureAwait(false),
            idOrName));

    /// <inheritdoc />
    public async IAsyncEnumerable<ContainerStatsSample> StreamStatsAsync(string idOrName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var sample in _client.Containers.StreamStatsAsync(idOrName, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return ContainerMapper.ToStatsSample(sample, idOrName);
        }
    }

    /// <inheritdoc />
    public Task<CommandResult> RunCommandAsync(string idOrName, IReadOnlyList<string> command,
        string user = null, string workingDir = null, IReadOnlyList<string> env = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => ContainerMapper.ToCommandResult(
            await _client.Containers.ExecAsync(idOrName, command, user, workingDir, env,
                cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<IReadOnlyList<ImageInfo>> ListImagesAsync(bool all = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var images = await _client.Images.ListAsync(all, cancellationToken).ConfigureAwait(false);
            var mapped = new List<ImageInfo>(images.Count);
            foreach (var image in images)
            {
                mapped.Add(ImageMapper.ToInfo(image));
            }

            return (IReadOnlyList<ImageInfo>)mapped;
        });

    /// <inheritdoc />
    public Task<ImageDetail> InspectImageAsync(string reference,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => ImageMapper.ToDetail(
            await _client.Images.InspectAsync(reference, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<IReadOnlyList<ImageLayerInfo>> GetImageHistoryAsync(string reference,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var history = await _client.Images.GetHistoryAsync(reference, cancellationToken)
                .ConfigureAwait(false);
            var mapped = new List<ImageLayerInfo>(history.Count);
            foreach (var entry in history)
            {
                mapped.Add(ImageMapper.ToLayer(entry));
            }

            return (IReadOnlyList<ImageLayerInfo>)mapped;
        });

    /// <inheritdoc />
    public Task PullImageAsync(string reference, IProgress<string> progress = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Images.PullAsync(reference, progress, cancellationToken));

    /// <inheritdoc />
    public Task RemoveImageAsync(string reference, bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Images.RemoveAsync(reference, force, cancellationToken));

    /// <inheritdoc />
    public Task TagImageAsync(string sourceReference, string targetReference,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Images.TagAsync(sourceReference, targetReference, cancellationToken));

    /// <inheritdoc />
    public Task PruneImagesAsync(bool dangling = true, CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Images.PruneAsync(dangling, cancellationToken));

    /// <inheritdoc />
    public Task<ImageBuildOutcome> BuildImageAsync(ImageBuildRequest request,
        IProgress<string> progress = null, CancellationToken cancellationToken = default) =>
        RunAsync(async () => ImageMapper.ToBuildOutcome(
            await _client.Images.BuildAsync(ImageMapper.ToBuildSpec(request, progress),
                cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<IReadOnlyList<NetworkInfo>> ListNetworksAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var networks = await _client.Networks.ListAsync(cancellationToken).ConfigureAwait(false);
            var mapped = new List<NetworkInfo>(networks.Count);
            foreach (var network in networks)
            {
                mapped.Add(NetworkVolumeMapper.ToInfo(network));
            }

            return (IReadOnlyList<NetworkInfo>)mapped;
        });

    /// <inheritdoc />
    public Task<NetworkInfo> InspectNetworkAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => NetworkVolumeMapper.ToInfo(
            await _client.Networks.InspectAsync(idOrName, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<string> CreateNetworkAsync(string name,
        IReadOnlyDictionary<string, string> labels = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Networks.CreateAsync(name, "bridge", Copy(labels), cancellationToken));

    /// <inheritdoc />
    public Task RemoveNetworkAsync(string idOrName, CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Networks.RemoveAsync(idOrName, cancellationToken));

    /// <inheritdoc />
    public Task ConnectContainerAsync(string network, string container,
        IReadOnlyList<string> aliases = null, CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Networks.ConnectAsync(network, container, aliases, cancellationToken));

    /// <inheritdoc />
    public Task DisconnectContainerAsync(string network, string container, bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Networks.DisconnectAsync(network, container, force, cancellationToken));

    /// <inheritdoc />
    public Task PruneNetworksAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Networks.PruneAsync(cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var volumes = await _client.Volumes.ListAsync(cancellationToken).ConfigureAwait(false);
            var mapped = new List<VolumeInfo>(volumes.Count);
            foreach (var volume in volumes)
            {
                mapped.Add(NetworkVolumeMapper.ToInfo(volume));
            }

            return (IReadOnlyList<VolumeInfo>)mapped;
        });

    /// <inheritdoc />
    public Task<VolumeInfo> InspectVolumeAsync(string name,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => NetworkVolumeMapper.ToInfo(
            await _client.Volumes.InspectAsync(name, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<string> CreateVolumeAsync(string name,
        IReadOnlyDictionary<string, string> labels = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Volumes.CreateAsync(name, Copy(labels), cancellationToken));

    /// <inheritdoc />
    public Task RemoveVolumeAsync(string name, bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Volumes.RemoveAsync(name, force, cancellationToken));

    /// <inheritdoc />
    public Task PruneVolumesAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Volumes.PruneAsync(cancellationToken));

    /// <inheritdoc />
    public Task<DiagnosticsReport> DiagnoseAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => DiagnosticsMapper.ToReport(
            await _client.Diagnostics.DiagnoseAsync(idOrName, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<CpuThrottlingInfo> GetCpuThrottlingAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => DiagnosticsMapper.ToInfo(
            await _client.Diagnostics.GetCpuThrottlingAsync(idOrName, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<MemoryBreakdownInfo> GetMemoryBreakdownAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => DiagnosticsMapper.ToInfo(
            await _client.Diagnostics.GetMemoryBreakdownAsync(idOrName, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<OomInfo> CheckOomAsync(string idOrName, CancellationToken cancellationToken = default) =>
        RunAsync(async () => DiagnosticsMapper.ToInfo(
            await _client.Diagnostics.CheckOomAsync(idOrName, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<HealthInfo> GetHealthAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => DiagnosticsMapper.ToInfo(
            await _client.Diagnostics.GetHealthAsync(idOrName, cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task WaitForHealthyAsync(string idOrName, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => _client.Diagnostics.WaitForHealthyAsync(idOrName, timeout, cancellationToken));

    /// <inheritdoc />
    public Task<IReadOnlyList<AdvisorFindingInfo>> AdviseContainerAsync(string idOrName,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => MapFindings(
            await _client.Advisor.AnalyzeContainerAsync(idOrName, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<IReadOnlyList<AdvisorFindingInfo>> AdviseAllContainersAsync(
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => MapFindings(
            await _client.Advisor.AnalyzeAllContainersAsync(cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<ImageScanReport> ScanImageAsync(string reference, ImageScanOptions options = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => AnalysisMapper.ToReport(
            await _client.Analysis.ScanImageAsync(reference, AnalysisMapper.ToScanOptions(options),
                cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<ImageEfficiencyReport> AnalyzeImageEfficiencyAsync(string reference,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => AnalysisMapper.ToReport(
            await _client.Analysis.AnalyzeImageEfficiencyAsync(reference, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<DockerfileLintReport> LintDockerfileAsync(string dockerfilePath,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => AnalysisMapper.ToReport(
            await _client.Analysis.LintDockerfileAsync(dockerfilePath, cancellationToken)
                .ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<ImageOptimizeReport> OptimizeImageAsync(string reference,
        ImageOptimizeOptions options = null, CancellationToken cancellationToken = default) =>
        RunAsync(async () => AnalysisMapper.ToReport(
            await _client.Analysis.OptimizeImageAsync(reference, AnalysisMapper.ToSlimOptions(options),
                cancellationToken).ConfigureAwait(false)));

    /// <inheritdoc />
    public Task<ShellProbeResult> ProbeShellAsync(string idOrName,
        IReadOnlyList<string> candidates = null, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var detail = await _client.Containers.InspectAsync(idOrName, cancellationToken)
                .ConfigureAwait(false);
            if (!detail.IsRunning)
            {
                return new ShellProbeResult
                {
                    Found = false,
                    Tried = [],
                    Message = "Start the container before opening a console.",
                };
            }

            return await ShellProber.ProbeAsync(_client, idOrName, candidates, cancellationToken)
                .ConfigureAwait(false);
        });

    /// <inheritdoc />
    public Task<IExecSession> OpenShellAsync(string idOrName, ExecSessionOptions options = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var settings = options ?? new ExecSessionOptions();
            var detail = await _client.Containers.InspectAsync(idOrName, cancellationToken)
                .ConfigureAwait(false);
            if (!detail.IsRunning)
            {
                throw new DockerManagementException(
                    "Start the container before opening a console.");
            }

            var probe = await ShellProber.ProbeAsync(_client, idOrName, settings.ShellCandidates,
                cancellationToken).ConfigureAwait(false);
            if (!probe.Found)
            {
                throw new NoShellAvailableException(
                    ShellProber.DescribeFailure(detail.Config?.Image ?? detail.Image, probe))
                {
                    Result = probe,
                };
            }

            var spec = new ExecSpec
            {
                Command = [probe.ShellPath],
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                ConsoleHeight = settings.Rows,
                ConsoleWidth = settings.Columns,
                WorkingDir = settings.WorkingDir,
                User = settings.User,
            };

            foreach (var variable in settings.Env ?? [])
            {
                spec.Env.Add(variable);
            }

            var stream = await _client.Containers.ExecStreamAsync(idOrName, spec, cancellationToken)
                .ConfigureAwait(false);
            return (IExecSession)new ExecSession(_client.Containers, stream, detail.Id,
                probe.ShellPath);
        });

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _client.Dispose();
        }
    }

    private Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        IDictionary<string, string> labelFilters, bool includeStopped,
        CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            var containers = await _client.Containers
                .ListAsync(includeStopped, labelFilters, cancellationToken).ConfigureAwait(false);
            var mapped = new List<ContainerInfo>(containers.Count);
            foreach (var container in containers)
            {
                mapped.Add(ContainerMapper.ToInfo(container));
            }

            return (IReadOnlyList<ContainerInfo>)mapped;
        });

    private static IReadOnlyList<AdvisorFindingInfo> MapFindings(
        IReadOnlyList<AdvisorFinding> findings)
    {
        var mapped = new List<AdvisorFindingInfo>(findings.Count);
        foreach (var finding in findings)
        {
            mapped.Add(DiagnosticsMapper.ToInfo(finding));
        }

        return mapped;
    }

    private static IDictionary<string, string> Copy(IReadOnlyDictionary<string, string> labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return null;
        }

        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            copy[label.Key] = label.Value;
        }

        return copy;
    }

    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldTranslate(exception))
        {
            throw Translate(exception);
        }
    }

    private static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldTranslate(exception))
        {
            throw Translate(exception);
        }
    }

    private static bool ShouldTranslate(Exception exception) =>
        exception is DockerException && exception is not OperationCanceledException;

    private static DockerManagementException Translate(Exception exception) => exception switch
    {
        DockerContainerNotFoundException notFound => new DockerManagementException(
            notFound.Message, notFound)
        {
            IsNotFound = true,
            StatusCode = (int)notFound.StatusCode,
            Detail = notFound.ResponseBody,
        },
        DockerImageNotFoundException notFound => new DockerManagementException(
            notFound.Message, notFound)
        {
            IsNotFound = true,
            StatusCode = (int)notFound.StatusCode,
            Detail = notFound.ResponseBody,
        },
        DockerApiException api => new DockerManagementException(api.Message, api)
        {
            IsNotFound = api.StatusCode == HttpStatusCode.NotFound,
            StatusCode = (int)api.StatusCode,
            Detail = api.ResponseBody,
        },
        DockerCliException cli => new DockerManagementException(cli.Message, cli)
        {
            Detail = cli.StdErr,
        },
        _ => new DockerManagementException(exception.Message, exception),
    };
}
