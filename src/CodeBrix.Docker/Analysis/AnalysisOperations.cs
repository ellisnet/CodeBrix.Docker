using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Image analysis performed by running Trivy, Dive, Hadolint and Slim as containers, so that none of
/// these tools has to be installed on the machine running the code.
/// </summary>
/// <remarks>
/// <para>
/// Every tool container carries the label <c>codebrix.docker.tool=true</c>, is named with the prefix
/// <c>codebrix-tool-</c>, and is removed in a <see langword="finally"/> block — including when the tool
/// fails or the operation is cancelled. The tool image is pulled on demand the first time it is used.
/// </para>
/// <para>
/// Trivy, Dive and Slim need the Docker socket bind-mounted so that they can read the daemon's images;
/// Hadolint does not, because the Dockerfile is copied into its container instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var docker = DockerClient.Create();
/// var scan = await docker.Analysis.ScanImageAsync("alpine:3.19");
/// Console.WriteLine($"{scan.Total} vulnerabilities, {scan.CountOf("CRITICAL")} critical");
/// </code>
/// </example>
public sealed class AnalysisOperations
{
    /// <summary>The label name every container started by this class carries.</summary>
    public const string ToolLabelName = "codebrix.docker.tool";

    /// <summary>The value of the <see cref="ToolLabelName"/> label.</summary>
    public const string ToolLabelValue = "true";

    /// <summary>The name prefix every container started by this class carries.</summary>
    public const string ContainerNamePrefix = "codebrix-tool-";

    /// <summary>The default name of the volume caching Trivy's vulnerability database.</summary>
    public const string DefaultTrivyCacheVolumeName = "codebrix-docker-trivy-cache";

    private const string DockerSocketPath = "/var/run/docker.sock";
    private const string TrivyCachePath = "/root/.cache";
    private const string DiveExportPath = "/tmp/dive.json";
    private const string HadolintTargetPath = "/Dockerfile";

    private readonly DockerApiClient _api;
    private readonly ContainerOperations _containers;
    private readonly DockerCliRunner _cli;

    internal AnalysisOperations(DockerApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _containers = new ContainerOperations(api);
        _cli = new DockerCliRunner(api.Options);
    }

    /// <summary>Gets or sets the Trivy image to run. Defaults to <c>aquasec/trivy:latest</c>.</summary>
    public string TrivyImage { get; set; } = "aquasec/trivy:latest";

    /// <summary>Gets or sets the Dive image to run. Defaults to <c>wagoodman/dive:latest</c>.</summary>
    public string DiveImage { get; set; } = "wagoodman/dive:latest";

    /// <summary>Gets or sets the Hadolint image to run. Defaults to <c>hadolint/hadolint:latest</c>.</summary>
    public string HadolintImage { get; set; } = "hadolint/hadolint:latest";

    /// <summary>
    /// Gets or sets the Slim image to run. Defaults to <c>mintoolkit/mint:latest</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The upstream project renamed itself from <c>slim</c> to <c>mint</c>, and the old
    /// <c>dslim/slim</c> repository stopped receiving builds at 1.40.11 (February 2024). That build
    /// negotiates Docker Engine API version 1.24, which modern daemons refuse outright — against Docker
    /// 29 it fails immediately with <c>client version 1.24 is too old. Minimum supported API version is
    /// 1.40</c>, before it inspects anything. Setting <c>DOCKER_API_VERSION</c> does not help, because
    /// the version is baked into that build's client.
    /// </para>
    /// <para>
    /// <c>mintoolkit/mint</c> is the maintained continuation and accepts the identical command line, so
    /// it is the default here. Assign <c>dslim/slim:latest</c> — or use
    /// <see cref="SlimOptions.ToolImage"/> for a single run — to go back to the retired image against an
    /// older daemon.
    /// </para>
    /// </remarks>
    public string SlimImage { get; set; } = "mintoolkit/mint:latest";

    // ---------------------------------------------------------------------------------------
    // Trivy
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Scans an image for known vulnerabilities with Trivy.
    /// </summary>
    /// <param name="imageReference">The image to scan, for example <c>alpine:3.19</c>.</param>
    /// <param name="options">Optional scan options. When omitted, defaults apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The vulnerabilities Trivy found, with per-severity counts.</returns>
    /// <remarks>
    /// The first scan on a machine downloads Trivy's vulnerability database, which can take several
    /// minutes; the database is kept in the named volume
    /// <see cref="TrivyScanOptions.CacheVolumeName"/> so later scans start immediately. No timeout is
    /// applied unless <see cref="TrivyScanOptions.Timeout"/> asks for one — cancel through
    /// <paramref name="cancellationToken"/> instead.
    /// </remarks>
    /// <exception cref="DockerException">Trivy produced no report that could be parsed.</exception>
    public async Task<TrivyScanResult> ScanImageAsync(string imageReference, TrivyScanOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);
        options ??= new TrivyScanOptions();

        var toolImage = string.IsNullOrWhiteSpace(options.ToolImage) ? TrivyImage : options.ToolImage;
        await EnsureImageAsync(toolImage, cancellationToken).ConfigureAwait(false);
        await EnsureCacheVolumeAsync(options.CacheVolumeName, cancellationToken).ConfigureAwait(false);

        var command = new List<string> { "image", "--format", "json", "--quiet" };
        if (options.IgnoreUnfixed)
        {
            command.Add("--ignore-unfixed");
        }

        if (options.Severities.Count > 0)
        {
            command.Add("--severity");
            command.Add(string.Join(',', options.Severities.Select(s => s.ToUpperInvariant())));
        }

        command.Add(imageReference);

        var spec = new ContainerSpec
        {
            Image = toolImage,
            Name = NewContainerName("trivy"),
            Command = command,
            Labels = { [ToolLabelName] = ToolLabelValue },
            Mounts =
            {
                MountSpec.Bind(DockerSocketPath, DockerSocketPath),
                MountSpec.Volume(options.CacheVolumeName, TrivyCachePath),
            },
        };

        using var timeoutSource = CreateTimeoutSource(options.Timeout, cancellationToken);
        var token = timeoutSource?.Token ?? cancellationToken;

        string containerId = null;
        try
        {
            containerId = await CreateToolContainerAsync(spec, token).ConfigureAwait(false);
            await _containers.StartAsync(containerId, token).ConfigureAwait(false);

            var exitCode = await _containers.WaitForExitAsync(containerId, token).ConfigureAwait(false);
            var logs = await _containers.GetLogsAsync(containerId, cancellationToken: token).ConfigureAwait(false);

            return TrivyOutputParser.Parse(imageReference, logs.Stdout, logs.Stderr, exitCode);
        }
        catch (OperationCanceledException) when (IsTimeout(timeoutSource, cancellationToken))
        {
            throw new DockerException(
                $"Trivy did not finish scanning '{imageReference}' within {options.Timeout}.");
        }
        finally
        {
            await RemoveToolContainerAsync(containerId).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Dive
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Measures how much space an image wastes across its layers, using Dive.
    /// </summary>
    /// <param name="imageReference">The image to analyze, for example <c>alpine:3.19</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The efficiency score, wasted bytes and per-layer breakdown.</returns>
    /// <remarks>
    /// Dive writes its report inside its own container and the file is retrieved with
    /// <c>docker cp</c> afterwards, rather than through a bind-mounted host directory — bind-mounting a
    /// Windows host path into a Linux container is not reliable across machines. The image being
    /// analyzed must be available to the daemon; it is pulled first when it is not.
    /// </remarks>
    /// <exception cref="DockerException">Dive produced no report that could be parsed.</exception>
    public async Task<DiveAnalysisResult> AnalyzeImageEfficiencyAsync(string imageReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);

        await EnsureImageAsync(DiveImage, cancellationToken).ConfigureAwait(false);
        await EnsureImageAsync(imageReference, cancellationToken).ConfigureAwait(false);

        var spec = new ContainerSpec
        {
            Image = DiveImage,
            Name = NewContainerName("dive"),
            Command = [imageReference, "--json", DiveExportPath],
            // Without CI mode Dive starts its full-screen terminal interface and never exits.
            Env = { "CI=true" },
            Labels = { [ToolLabelName] = ToolLabelValue },
            Mounts = { MountSpec.Bind(DockerSocketPath, DockerSocketPath) },
        };

        var localExportPath = Path.Combine(Path.GetTempPath(),
            $"codebrix-dive-{Guid.NewGuid().ToString("N")[..12]}.json");

        string containerId = null;
        try
        {
            containerId = await CreateToolContainerAsync(spec, cancellationToken).ConfigureAwait(false);
            await _containers.StartAsync(containerId, cancellationToken).ConfigureAwait(false);

            var exitCode = await _containers.WaitForExitAsync(containerId, cancellationToken).ConfigureAwait(false);
            var logs = await _containers.GetLogsAsync(containerId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await _cli.RunAsync(["cp", $"{containerId}:{DiveExportPath}", localExportPath],
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (DockerCliException ex)
            {
                throw new DockerException(
                    $"Dive did not write a report for '{imageReference}' (exit code {exitCode}). " +
                    $"Output: {AnalysisJson.Describe(logs.Stdout, logs.Stderr)}", ex);
            }

            var json = await File.ReadAllTextAsync(localExportPath, cancellationToken).ConfigureAwait(false);
            return DiveOutputParser.Parse(imageReference, json, exitCode);
        }
        finally
        {
            await RemoveToolContainerAsync(containerId).ConfigureAwait(false);
            DeleteQuietly(localExportPath);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Hadolint
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Lints a Dockerfile with Hadolint.
    /// </summary>
    /// <param name="dockerfilePath">The path of the Dockerfile on this machine.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rule violations Hadolint found.</returns>
    /// <remarks>
    /// The Dockerfile is copied into a created-but-not-yet-started container with <c>docker cp</c>,
    /// which keeps the operation working regardless of how host paths map into the daemon.
    /// Hadolint runs with <c>--no-fail</c>, so findings come back as data rather than as a non-zero
    /// exit code.
    /// </remarks>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="dockerfilePath"/>.</exception>
    /// <exception cref="DockerException">Hadolint produced no report that could be parsed.</exception>
    public async Task<HadolintResult> LintDockerfileAsync(string dockerfilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerfilePath);

        var fullPath = Path.GetFullPath(dockerfilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"There is no Dockerfile at '{fullPath}'.", fullPath);
        }

        await EnsureImageAsync(HadolintImage, cancellationToken).ConfigureAwait(false);

        var spec = new ContainerSpec
        {
            Image = HadolintImage,
            Name = NewContainerName("hadolint"),
            Command = ["/bin/hadolint", "--format", "json", "--no-fail", HadolintTargetPath],
            Labels = { [ToolLabelName] = ToolLabelValue },
        };

        string containerId = null;
        try
        {
            containerId = await CreateToolContainerAsync(spec, cancellationToken).ConfigureAwait(false);

            await _cli.RunAsync(["cp", fullPath, $"{containerId}:{HadolintTargetPath}"],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _containers.StartAsync(containerId, cancellationToken).ConfigureAwait(false);
            var exitCode = await _containers.WaitForExitAsync(containerId, cancellationToken).ConfigureAwait(false);
            var logs = await _containers.GetLogsAsync(containerId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return HadolintOutputParser.Parse(fullPath, logs.Stdout, logs.Stderr, exitCode);
        }
        finally
        {
            await RemoveToolContainerAsync(containerId).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Slim
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds an image with only the files its container was observed using, producing a much smaller
    /// image, using Slim.
    /// </summary>
    /// <param name="imageReference">The image to optimize.</param>
    /// <param name="options">Optional options. When omitted, defaults apply.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The name of the optimized image and whether Slim succeeded.</returns>
    /// <remarks>
    /// <para>
    /// EXPERIMENTAL. Slim decides what to keep by watching a temporary container run for a few seconds,
    /// so anything the application only touches on a code path that was not exercised during that window
    /// can be missing from the optimized image. Always test an optimized image before deploying it, and
    /// treat both the arguments and the result shape of this method as subject to change.
    /// </para>
    /// <para>
    /// Probing is disabled unless <see cref="SlimOptions.HttpProbePaths"/> lists paths, because probing
    /// an image that does not serve HTTP only slows the run down.
    /// </para>
    /// </remarks>
    public async Task<SlimResult> OptimizeImageAsync(string imageReference, SlimOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);
        options ??= new SlimOptions();

        var toolImage = string.IsNullOrWhiteSpace(options.ToolImage) ? SlimImage : options.ToolImage;
        var outputTag = string.IsNullOrWhiteSpace(options.OutputTag)
            ? imageReference + ".slim"
            : options.OutputTag;

        await EnsureImageAsync(toolImage, cancellationToken).ConfigureAwait(false);
        await EnsureImageAsync(imageReference, cancellationToken).ConfigureAwait(false);

        var originalSize = await TryGetImageSizeAsync(imageReference, cancellationToken).ConfigureAwait(false);

        var spec = new ContainerSpec
        {
            Image = toolImage,
            Name = NewContainerName("slim"),
            Command = BuildSlimCommand(imageReference, outputTag, options),
            Labels = { [ToolLabelName] = ToolLabelValue },
            Mounts = { MountSpec.Bind(DockerSocketPath, DockerSocketPath) },
        };

        using var timeoutSource = CreateTimeoutSource(options.Timeout, cancellationToken);
        var token = timeoutSource?.Token ?? cancellationToken;

        string containerId = null;
        try
        {
            containerId = await CreateToolContainerAsync(spec, token).ConfigureAwait(false);
            await _containers.StartAsync(containerId, token).ConfigureAwait(false);

            var exitCode = await _containers.WaitForExitAsync(containerId, token).ConfigureAwait(false);
            var logs = await _containers.GetLogsAsync(containerId, cancellationToken: token).ConfigureAwait(false);

            var optimizedSize = await TryGetImageSizeAsync(outputTag, token).ConfigureAwait(false);

            return new SlimResult
            {
                OriginalImage = imageReference,
                OptimizedImage = outputTag,
                Succeeded = exitCode == 0 && optimizedSize.HasValue,
                ExitCode = exitCode,
                OriginalSizeBytes = originalSize,
                OptimizedSizeBytes = optimizedSize,
                Output = logs.Combined,
            };
        }
        catch (OperationCanceledException) when (IsTimeout(timeoutSource, cancellationToken))
        {
            throw new DockerException(
                $"Slim did not finish optimizing '{imageReference}' within {options.Timeout}.");
        }
        finally
        {
            await RemoveToolContainerAsync(containerId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds Slim's argument list. Kept separate so that the argument shape can be verified without
    /// running the tool.
    /// </summary>
    internal static IReadOnlyList<string> BuildSlimCommand(string imageReference, string outputTag,
        SlimOptions options)
    {
        var command = new List<string>
        {
            "build",
            "--target",
            imageReference,
            "--tag",
            outputTag,
            "--continue-after=" + options.ContinueAfterSeconds.ToString(CultureInfo.InvariantCulture),
        };

        if (options.HttpProbePaths.Count == 0)
        {
            command.Add("--http-probe=false");
        }
        else
        {
            foreach (var path in options.HttpProbePaths)
            {
                command.Add("--http-probe-cmd");
                command.Add(path);
            }
        }

        return command;
    }

    // ---------------------------------------------------------------------------------------
    // Tool container plumbing
    // ---------------------------------------------------------------------------------------

    private static string NewContainerName(string tool) =>
        $"{ContainerNamePrefix}{tool}-{Guid.NewGuid().ToString("N")[..12]}";

    private async Task<string> CreateToolContainerAsync(ContainerSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            return await _containers.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerImageNotFoundException)
        {
            // The tool image disappeared between the availability check and the create call.
            await PullImageAsync(spec.Image, cancellationToken).ConfigureAwait(false);
            return await _containers.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes a tool container on a token of its own, so that cleanup still runs after the caller's
    /// token has been cancelled. Failures here are swallowed: they must not mask the real error.
    /// </summary>
    private async Task RemoveToolContainerAsync(string containerId)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            return;
        }

        try
        {
            using var cleanupSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _containers.RemoveAsync(containerId, force: true, removeVolumes: false, cleanupSource.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort. A container that cannot be removed is left carrying the tool label so that
            // a later prune finds it.
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A temporary file that cannot be deleted is not worth failing the analysis over.
        }
    }

    private static CancellationTokenSource CreateTimeoutSource(TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is not { } value || value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(value);
        return source;
    }

    private static bool IsTimeout(CancellationTokenSource timeoutSource, CancellationToken cancellationToken) =>
        timeoutSource is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested;

    // ---------------------------------------------------------------------------------------
    // Images and volumes
    // ---------------------------------------------------------------------------------------

    private async Task EnsureImageAsync(string reference, CancellationToken cancellationToken)
    {
        if (await _api.TryGetAsync($"images/{reference}/json", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await PullImageAsync(reference, cancellationToken).ConfigureAwait(false);
    }

    private async Task PullImageAsync(string reference, CancellationToken cancellationToken)
    {
        var (name, tag) = SplitReference(reference);
        var query = new QueryStringBuilder().Add("fromImage", name).Add("tag", tag);

        // The pull is a JSON-lines progress stream; it must be read to the end or the daemon aborts it.
        await using var stream = await _api
            .PostForStreamAsync(query.AppendTo("images/create"), body: null, cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || line.AsSpan().IsWhiteSpace())
            {
                continue;
            }

            AnalysisPullProgress progress;
            try
            {
                progress = DockerJson.Deserialize<AnalysisPullProgress>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (progress?.Error is { Length: > 0 } error)
            {
                throw new DockerException($"Pulling image '{reference}' failed: {error}");
            }
        }
    }

    private async Task EnsureCacheVolumeAsync(string volumeName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeName);

        if (await _api.TryGetAsync($"volumes/{Uri.EscapeDataString(volumeName)}", cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var request = new AnalysisVolumeCreateRequest
        {
            Name = volumeName,
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ToolLabelName] = ToolLabelValue,
            },
        };

        await _api.PostAsync("volumes/create", request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long?> TryGetImageSizeAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var info = await _api
                .GetAsync<AnalysisImageInfo>($"images/{reference}/json", cancellationToken)
                .ConfigureAwait(false);
            return info.Size;
        }
        catch (DockerException)
        {
            return null;
        }
    }

    /// <summary>
    /// Splits an image reference into the name and tag (or digest) that <c>POST /images/create</c> wants
    /// as separate query parameters.
    /// </summary>
    internal static (string Name, string Tag) SplitReference(string reference)
    {
        var digestIndex = reference.IndexOf('@', StringComparison.Ordinal);
        if (digestIndex > 0)
        {
            return (reference[..digestIndex], reference[(digestIndex + 1)..]);
        }

        var colonIndex = reference.LastIndexOf(':');
        var slashIndex = reference.LastIndexOf('/');
        return colonIndex > slashIndex && colonIndex > 0
            ? (reference[..colonIndex], reference[(colonIndex + 1)..])
            : (reference, "latest");
    }
}
