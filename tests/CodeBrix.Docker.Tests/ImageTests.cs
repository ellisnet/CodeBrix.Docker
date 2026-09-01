using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class ImageTests(DockerTestFixture fixture)
{
    private const string Dockerfile = """
        FROM busybox:latest AS base
        LABEL codebrix.docker.stage=base
        RUN echo "base stage" > /base.txt

        FROM base AS final
        RUN echo "final stage" > /final.txt
        """;

    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task PullAsync_ReportsProgressWhileFetchingAnImage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var progress = new CollectingProgress();

        await Client.Images.PullAsync("busybox:latest", progress, cancellation.Token);

        Assert.NotEmpty(progress.Lines);
        Assert.All(progress.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line)));

        var images = await Client.Images.ListAsync(all: false, cancellation.Token);
        Assert.Contains(images, image => image.RepoTags?.Contains("busybox:latest") == true);
    }

    [Fact]
    public async Task BuildAsync_ProducesATaggedLabelledImageAndABuildLog()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", Dockerfile);

        var tag = $"{DockerTestFixture.ImageRepositoryPrefix}build:latest";
        var extraTag = $"{DockerTestFixture.ImageRepositoryPrefix}build:copied";
        var progress = new CollectingProgress();
        var spec = new ImageBuildSpec
        {
            ContextDirectory = context.Path,
            Tags = { tag },
            Labels =
            {
                [DockerTestFixture.LabelName] = DockerTestFixture.LabelValue,
            },
            Output = progress,
        };

        try
        {
            var result = await Client.Images.BuildAsync(spec, cancellation.Token);

            Assert.False(string.IsNullOrWhiteSpace(result.ImageId));
            Assert.StartsWith("sha256:", result.ImageId, StringComparison.Ordinal);
            Assert.Equal(12, result.ShortImageId.Length);
            Assert.Contains(tag, result.Tags);

            // BuildKit writes its progress to stderr, so the captured Output is the only place the
            // build log appears.
            Assert.NotEmpty(result.Output);
            Assert.Contains("busybox", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(progress.Lines);

            var inspect = await Client.Images.InspectAsync(tag, cancellation.Token);
            Assert.Equal(result.ImageId, inspect.Id);
            Assert.True(inspect.Size > 0);
            Assert.Contains(tag, inspect.RepoTags ?? []);
            Assert.Equal("linux", inspect.Os);
            Assert.True(inspect.LayerCount >= 1);
            Assert.NotNull(inspect.Config?.Labels);
            Assert.Equal(DockerTestFixture.LabelValue, inspect.Config.Labels[DockerTestFixture.LabelName]);
            Assert.NotNull(inspect.Created);

            var history = await Client.Images.GetHistoryAsync(tag, cancellation.Token);
            Assert.NotEmpty(history);
            Assert.Contains(history, entry => entry.Size > 0);

            var summaries = await Client.Images.ListAsync(all: false, DockerTestFixture.TestLabelFilter,
                cancellation.Token);
            var summary = Assert.Single(summaries, image => image.RepoTags?.Contains(tag) == true);
            Assert.True(summary.CreatedUnixSeconds > 0);
            Assert.NotNull(summary.Created);
            Assert.True(summary.Size > 0);

            await Client.Images.TagAsync(tag, extraTag, cancellation.Token);
            var retagged = await Client.Images.ListAsync(all: false, DockerTestFixture.TestLabelFilter,
                cancellation.Token);
            Assert.Contains(retagged, image => image.RepoTags?.Contains(extraTag) == true);

            await Client.Images.RemoveAsync(extraTag, force: true, cancellation.Token);
            await Client.Images.RemoveAsync(tag, force: true, cancellation.Token);

            await Assert.ThrowsAsync<DockerImageNotFoundException>(
                () => Client.Images.InspectAsync(tag, cancellation.Token));
        }
        finally
        {
            await fixture.RemoveImageQuietlyAsync(extraTag);
            await fixture.RemoveImageQuietlyAsync(tag);
        }
    }

    [Fact]
    public async Task BuildAsync_WithTarget_StopsAtTheRequestedStage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", Dockerfile);

        var tag = $"{DockerTestFixture.ImageRepositoryPrefix}staged:base";
        var spec = new ImageBuildSpec
        {
            ContextDirectory = context.Path,
            Tags = { tag },
            Target = "base",
            Labels =
            {
                [DockerTestFixture.LabelName] = DockerTestFixture.LabelValue,
            },
        };
        string containerId = null;

        try
        {
            var result = await Client.Images.BuildAsync(spec, cancellation.Token);
            Assert.False(string.IsNullOrWhiteSpace(result.ImageId));

            var probe = fixture.Spec("staged", tag, "sh", "-c",
                "test -f /base.txt && test ! -f /final.txt && echo stage-is-base");
            containerId = await Client.Containers.RunAsync(probe, cancellation.Token);

            var exitCode = await Client.Containers.WaitForExitAsync(containerId, cancellation.Token);
            var logs = await Client.Containers.GetLogsAsync(containerId, cancellationToken: cancellation.Token);

            Assert.Equal(0, exitCode);
            Assert.Contains("stage-is-base", logs.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(containerId);
            await fixture.RemoveImageQuietlyAsync(tag);
        }
    }

    [Fact]
    public async Task BuildAsync_WithABuildArgument_PassesItToTheDockerfile()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", """
            FROM busybox:latest
            ARG GREETING=unset
            RUN echo "$GREETING" > /greeting.txt
            """);

        var tag = $"{DockerTestFixture.ImageRepositoryPrefix}buildarg:latest";
        var spec = new ImageBuildSpec
        {
            ContextDirectory = context.Path,
            Tags = { tag },
            BuildArgs = { ["GREETING"] = "configured-by-build-arg" },
            Labels =
            {
                [DockerTestFixture.LabelName] = DockerTestFixture.LabelValue,
            },
        };
        string containerId = null;

        try
        {
            await Client.Images.BuildAsync(spec, cancellation.Token);

            var probe = fixture.Spec("buildarg", tag, "cat", "/greeting.txt");
            containerId = await Client.Containers.RunAsync(probe, cancellation.Token);
            await Client.Containers.WaitForExitAsync(containerId, cancellation.Token);
            var logs = await Client.Containers.GetLogsAsync(containerId, cancellationToken: cancellation.Token);

            Assert.Contains("configured-by-build-arg", logs.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(containerId);
            await fixture.RemoveImageQuietlyAsync(tag);
        }
    }

    [Fact]
    public async Task BuildAsync_WithoutATag_IsRejected()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var context = new TempDirectory();
        context.WriteFile("Dockerfile", Dockerfile);

        var spec = new ImageBuildSpec { ContextDirectory = context.Path };

        await Assert.ThrowsAsync<ArgumentException>(() => Client.Images.BuildAsync(spec, cancellation.Token));
    }

    [Fact]
    public async Task InspectAsync_ForAnUnknownReference_ThrowsImageNotFound()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        await Assert.ThrowsAsync<DockerImageNotFoundException>(
            () => Client.Images.InspectAsync($"{DockerTestFixture.ImageRepositoryPrefix}absent:missing",
                cancellation.Token));
    }

    [Fact]
    public async Task InspectAsync_ForABaseImage_SurfacesItsConfiguration()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var inspect = await Client.Images.InspectAsync("alpine:3.19", cancellation.Token);

        Assert.StartsWith("sha256:", inspect.Id, StringComparison.Ordinal);
        Assert.True(inspect.Size > 0);
        Assert.Contains("alpine:3.19", inspect.RepoTags ?? []);
        Assert.Equal("linux", inspect.Os);
        Assert.False(string.IsNullOrWhiteSpace(inspect.Architecture));
        Assert.Equal(1, inspect.LayerCount);
        Assert.NotNull(inspect.Config);
        Assert.Contains(inspect.DisplayName, inspect.RepoTags);
    }
}
