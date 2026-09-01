using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class SlimTests(DockerTestFixture fixture)
{
    /// <summary>Environment variable that opts a run in to the slow, experimental optimizer.</summary>
    public const string GateVariable = "CODEBRIX_DOCKER_TEST_SLIM";

    private DockerClient Client => fixture.Client;

    [EnvGatedFact(GateVariable)]
    public async Task OptimizeImageAsync_ProducesASmallerImage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var outputTag = $"{DockerTestFixture.ImageRepositoryPrefix}slim:latest";

        try
        {
            var result = await Client.Analysis.OptimizeImageAsync("nginx:alpine", new SlimOptions
            {
                OutputTag = outputTag,
                ContinueAfterSeconds = 10,
                Timeout = TimeSpan.FromMinutes(15),
            }, cancellation.Token);

            Assert.Equal("nginx:alpine", result.OriginalImage);
            Assert.Equal(outputTag, result.OptimizedImage);
            Assert.True(result.Succeeded, $"Slim reported exit code {result.ExitCode}: {result.Output}");
            Assert.NotNull(result.OriginalSizeBytes);
            Assert.NotNull(result.OptimizedSizeBytes);
            Assert.True(result.OptimizedSizeBytes < result.OriginalSizeBytes);
            Assert.NotNull(result.SizeReduction);
            Assert.True(result.SizeReduction > 0);
        }
        finally
        {
            await fixture.RemoveImageQuietlyAsync(outputTag);
        }
    }
}
