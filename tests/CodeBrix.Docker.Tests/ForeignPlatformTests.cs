using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Live tests for the platform options: an arm64 pull (<see cref="ImagePullOptions.Platform"/>), an arm64
/// container on an x86-64 engine (<see cref="ContainerSpec.Platform"/>) and an arm64 build
/// (<see cref="ImageBuildSpec.Platform"/>), all running under qemu-user emulation.
/// </summary>
/// <remarks>
/// Each test puts the engine back as it found it. Its containers and built images carry the suite's label
/// and the codebrix-test/ repository prefix. On the containerd image store an arm64 pull of
/// alpine:latest adds the arm64 variant's content to the shared alpine:latest image rather than adding a
/// tag, so when that variant was not present before the test, the test removes exactly that variant again
/// (<c>docker image rm --force --platform linux/arm64 alpine:latest</c>) and leaves the native one in place.
/// On the graph-driver store, where the pull re-points the tag, the native image is pulled back instead.
/// </remarks>
[Collection(DockerTestCollection.Name)]
public sealed class ForeignPlatformTests(DockerTestFixture fixture)
{
    private const string Platform = "linux/arm64";
    private const string BaseImage = "alpine:latest";

    private DockerClient Client => fixture.Client;

    [ForeignPlatformFact]
    public async Task PullAsync_WithAnArm64Platform_FetchesTheArm64Variant()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(5));
        await using var variant = await Arm64BaseVariant.CaptureAsync(Client, cancellation.Token);

        //Act
        await Client.Images.PullAsync(BaseImage, new ImagePullOptions { Platform = Platform }, progress: null,
            cancellation.Token);

        //Assert
        var architecture = await variant.InspectArchitectureAsync(cancellation.Token);
        architecture.Should().Be("arm64");
    }

    [ForeignPlatformFact]
    public async Task RunAsync_WithAnArm64Platform_RunsTheContainerAsAarch64()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(5));
        await using var variant = await Arm64BaseVariant.CaptureAsync(Client, cancellation.Token);
        await Client.Images.PullAsync(BaseImage, new ImagePullOptions { Platform = Platform }, progress: null,
            cancellation.Token);
        var spec = fixture.Spec("arm64", BaseImage, "uname", "-m");
        spec.Platform = Platform;
        string containerId = null;

        try
        {
            //Act
            containerId = await Client.Containers.RunAsync(spec, cancellation.Token);
            var exitCode = await Client.Containers.WaitForExitAsync(containerId, cancellation.Token);
            var logs = await Client.Containers.GetLogsAsync(containerId, cancellationToken: cancellation.Token);
            var inspect = await Client.Containers.InspectAsync(containerId, cancellation.Token);

            //Assert
            exitCode.Should().Be(0L);
            logs.Stdout.Trim().Should().Be("aarch64");
            inspect.Platform.Should().Be("linux");
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(containerId);
        }
    }

    [ForeignPlatformFact]
    public async Task BuildAsync_WithAnArm64Platform_BuildsAnArm64Image()
    {
        //Arrange
        using var cancellation = Timeout(TimeSpan.FromMinutes(10));
        await using var variant = await Arm64BaseVariant.CaptureAsync(Client, cancellation.Token);
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", "FROM alpine\n");
        var tag = $"{DockerTestFixture.ImageRepositoryPrefix}platform:arm64";
        var spec = new ImageBuildSpec
        {
            ContextDirectory = context.Path,
            Tags = { tag },
            Platform = Platform,
            Labels = { [DockerTestFixture.LabelName] = DockerTestFixture.LabelValue },
        };

        try
        {
            //Act
            var result = await Client.Images.BuildAsync(spec, cancellation.Token);
            var inspect = await Client.Images.InspectAsync(tag, cancellation.Token);

            //Assert
            result.ImageId.Should().StartWith("sha256:");
            inspect.Architecture.Should().Be("arm64");
            inspect.Os.Should().Be("linux");
        }
        finally
        {
            await fixture.RemoveImageQuietlyAsync(tag);
        }
    }

    private static CancellationTokenSource Timeout(TimeSpan timeout)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(timeout);
        return cancellation;
    }

    /// <summary>
    /// Records whether alpine:latest already carried its arm64 variant, and on dispose removes the
    /// variant again when the test was the one that added it.
    /// </summary>
    private sealed class Arm64BaseVariant : IAsyncDisposable
    {
        private readonly DockerClient _client;
        private readonly DockerCliRunner _cli = new(new DockerClientOptions());
        private bool _wasPresent;
        private bool _containerdStore;

        private Arm64BaseVariant(DockerClient client) => _client = client;

        public static async Task<Arm64BaseVariant> CaptureAsync(DockerClient client,
            CancellationToken cancellationToken)
        {
            var variant = new Arm64BaseVariant(client);
            var driverStatus = await variant._cli.TryRunAsync(["info", "--format", "{{json .DriverStatus}}"],
                cancellationToken: cancellationToken);
            variant._containerdStore = driverStatus.Stdout.Contains("io.containerd.snapshotter",
                StringComparison.Ordinal);
            variant._wasPresent = await variant.InspectArchitectureAsync(cancellationToken) == "arm64";
            return variant;
        }

        /// <summary>The architecture of alpine:latest's arm64 variant, or an empty string when it is absent.</summary>
        public async Task<string> InspectArchitectureAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<string> args = _containerdStore
                ? ["image", "inspect", "--platform", Platform, "--format", "{{.Architecture}}", BaseImage]
                : ["image", "inspect", "--format", "{{.Architecture}}", BaseImage];
            var result = await _cli.TryRunAsync(args, cancellationToken: cancellationToken);
            return result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            if (_wasPresent)
            {
                return;
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if (_containerdStore)
            {
                await _cli.TryRunAsync(["image", "rm", "--force", "--platform", Platform, BaseImage],
                    cancellationToken: cancellation.Token);
            }
            else
            {
                await _client.Images.PullAsync(BaseImage, progress: null, cancellation.Token);
            }
        }
    }
}
