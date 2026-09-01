using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Owns the single <see cref="DockerClient"/> shared by every test class and guarantees that the
/// daemon is left exactly as it was found: every container, network, volume and image the suite
/// creates carries the <c>codebrix.docker.tests</c> label (tool containers carry
/// <c>codebrix.docker.tool</c>), and both sets are force-removed here.
/// </summary>
public sealed class DockerTestFixture : IAsyncLifetime
{
    /// <summary>Name prefix carried by every resource the suite creates.</summary>
    public const string NamePrefix = "codebrix-test-";

    /// <summary>Label name carried by every resource the suite creates.</summary>
    public const string LabelName = "codebrix.docker.tests";

    /// <summary>Label value carried by every resource the suite creates.</summary>
    public const string LabelValue = "true";

    /// <summary>Repository prefix carried by every image the suite builds.</summary>
    public const string ImageRepositoryPrefix = "codebrix-test/";

    /// <summary>Images the suite runs containers from; pulled once, never removed.</summary>
    public static readonly string[] BaseImages =
    [
        "busybox:latest",
        "alpine:latest",
        "alpine:3.19",
        "nginx:alpine",
    ];

    private readonly SemaphoreSlim _sshdGate = new(1, 1);
    private DockerClient _client;
    private SshdTestHarness _sshd;

    /// <summary>Gets the shared client.</summary>
    public DockerClient Client =>
        _client ?? throw new InvalidOperationException("The Docker test fixture has not been initialized.");

    /// <summary>Gets a fresh label filter dictionary matching resources created by the suite.</summary>
    public static Dictionary<string, string> TestLabelFilter =>
        new(StringComparer.Ordinal) { [LabelName] = LabelValue };

    /// <summary>Gets a fresh label filter dictionary matching containers created by the analysis tools.</summary>
    public static Dictionary<string, string> ToolLabelFilter =>
        new(StringComparer.Ordinal) { [AnalysisOperations.ToolLabelName] = AnalysisOperations.ToolLabelValue };

    /// <summary>Builds a unique, prefixed resource name.</summary>
    public string NewName(string role) => $"{NamePrefix}{role}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Builds a labelled, uniquely named container spec.</summary>
    public ContainerSpec Spec(string role, string image, params string[] command) => new()
    {
        Image = image,
        Name = NewName(role),
        Command = command.Length > 0 ? command : null,
        Labels = { [LabelName] = LabelValue },
    };

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _client = DockerClient.Create();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        await Client.System.EnsureLinuxDaemonAsync(cancellation.Token);

        // A previous aborted run must not influence this one.
        await CleanupAsync(cancellation.Token);

        foreach (var image in BaseImages)
        {
            await Client.Images.PullAsync(image, progress: null, cancellation.Token);
        }
    }

    /// <summary>
    /// Gets the containerised SSH server used by the <c>ssh://</c> transport tests, building its images
    /// and starting its containers on first use.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The shared harness.</returns>
    public async Task<SshdTestHarness> GetSshdHarnessAsync(CancellationToken cancellationToken)
    {
        await _sshdGate.WaitAsync(cancellationToken);

        try
        {
            _sshd ??= await SshdTestHarness.StartAsync(this, cancellationToken);
            return _sshd;
        }
        finally
        {
            _sshdGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_client is null)
        {
            return;
        }

        // Only the scratch key and known_hosts files: the containers and images it created are labelled,
        // and the sweep below removes them with everything else.
        _sshd?.Dispose();
        _sshd = null;
        _sshdGate.Dispose();

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await CleanupAsync(cancellation.Token);
        }
        catch (Exception)
        {
            // Cleanup is best effort; a failure here must not mask a test result.
        }

        _client.Dispose();
        _client = null;
    }

    /// <summary>Force-removes a container, ignoring failures.</summary>
    public async Task RemoveContainerQuietlyAsync(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Client.Containers.RemoveAsync(idOrName, force: true, removeVolumes: false, cancellation.Token);
        }
        catch (Exception)
        {
            // Already gone, or removal in progress; the fixture sweep is the backstop.
        }
    }

    /// <summary>Removes a network, ignoring failures.</summary>
    public async Task RemoveNetworkQuietlyAsync(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Client.Networks.RemoveAsync(idOrName, cancellation.Token);
        }
        catch (Exception)
        {
            // Already gone; the fixture sweep is the backstop.
        }
    }

    /// <summary>Force-removes a volume, ignoring failures.</summary>
    public async Task RemoveVolumeQuietlyAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Client.Volumes.RemoveAsync(name, force: true, cancellation.Token);
        }
        catch (Exception)
        {
            // Already gone; the fixture sweep is the backstop.
        }
    }

    /// <summary>Force-removes an image reference, ignoring failures.</summary>
    public async Task RemoveImageQuietlyAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Client.Images.RemoveAsync(reference, force: true, cancellation.Token);
        }
        catch (Exception)
        {
            // Already gone; the fixture sweep is the backstop.
        }
    }

    /// <summary>Lists the suite's own containers, in any state.</summary>
    public async Task<IReadOnlyList<ContainerSummary>> ListOwnContainersAsync(CancellationToken cancellationToken)
    {
        var labelled = await Client.Containers.ListAsync(all: true, TestLabelFilter, cancellationToken);
        var tools = await Client.Containers.ListAsync(all: true, ToolLabelFilter, cancellationToken);
        var all = await Client.Containers.ListAsync(all: true, labelFilters: null, cancellationToken);

        var byId = new Dictionary<string, ContainerSummary>(StringComparer.Ordinal);
        foreach (var container in labelled.Concat(tools))
        {
            byId[container.Id] = container;
        }

        foreach (var container in all)
        {
            var name = container.DisplayName;
            if (name.StartsWith(NamePrefix, StringComparison.Ordinal)
                || name.StartsWith(AnalysisOperations.ContainerNamePrefix, StringComparison.Ordinal))
            {
                byId[container.Id] = container;
            }
        }

        return byId.Values.ToArray();
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        foreach (var container in await ListOwnContainersAsync(cancellationToken))
        {
            try
            {
                await Client.Containers.RemoveAsync(container.Id, force: true, removeVolumes: false,
                    cancellationToken);
            }
            catch (Exception)
            {
                // Keep sweeping; one stubborn container must not strand the rest.
            }
        }

        foreach (var filter in new[] { TestLabelFilter, ToolLabelFilter })
        {
            foreach (var network in await Client.Networks.ListAsync(filter, cancellationToken))
            {
                try
                {
                    await Client.Networks.RemoveAsync(network.Id, cancellationToken);
                }
                catch (Exception)
                {
                    // Keep sweeping.
                }
            }

            foreach (var volume in await Client.Volumes.ListAsync(filter, cancellationToken))
            {
                try
                {
                    await Client.Volumes.RemoveAsync(volume.Name, force: true, cancellationToken);
                }
                catch (Exception)
                {
                    // Keep sweeping.
                }
            }
        }

        // The Trivy database cache is created by the library itself, outside the test label.
        try
        {
            await Client.Volumes.RemoveAsync(AnalysisOperations.DefaultTrivyCacheVolumeName, force: true,
                cancellationToken);
        }
        catch (Exception)
        {
            // Not present, or in use; nothing else to do.
        }

        await RemoveOwnImagesAsync(cancellationToken);
    }

    private async Task RemoveOwnImagesAsync(CancellationToken cancellationToken)
    {
        var references = new List<string>();

        foreach (var image in await Client.Images.ListAsync(all: true, TestLabelFilter, cancellationToken))
        {
            references.AddRange(OwnTags(image));
            if (OwnTags(image).Count == 0)
            {
                references.Add(image.Id);
            }
        }

        // Belt and braces: a build that lost its label is still recognisable by its repository prefix.
        foreach (var image in await Client.Images.ListAsync(all: true, labelFilters: null, cancellationToken))
        {
            references.AddRange(OwnTags(image));
        }

        foreach (var reference in references.Distinct(StringComparer.Ordinal))
        {
            try
            {
                await Client.Images.RemoveAsync(reference, force: true, cancellationToken);
            }
            catch (Exception)
            {
                // Keep sweeping.
            }
        }

        static List<string> OwnTags(ImageSummary image) =>
            (image.RepoTags ?? [])
            .Where(tag => tag.StartsWith(ImageRepositoryPrefix, StringComparison.Ordinal))
            .ToList();
    }
}
