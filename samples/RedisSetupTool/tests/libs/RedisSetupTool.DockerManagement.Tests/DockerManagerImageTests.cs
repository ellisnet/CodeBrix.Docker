using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Image operations against the live daemon.</summary>
[Collection(RedisSetupToolCollection.Name)]
public class DockerManagerImageTests
{
    private readonly RedisSetupToolFixture _fixture;

    /// <summary>Creates the test class.</summary>
    /// <param name="fixture">The shared fixture.</param>
    public DockerManagerImageTests(RedisSetupToolFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The images the fixture pulled are listed.</summary>
    [Fact]
    public async Task ListImagesAsync_FindsTheImagesTheSuiteUses()
    {
        //Act
        var images = await _fixture.Docker.ListImagesAsync(false,
            TestContext.Current.CancellationToken);

        //Assert
        var tags = new List<string>();
        foreach (var image in images)
        {
            foreach (var tag in image.RepoTags)
            {
                tags.Add(tag);
            }
        }

        tags.Should().Contain("redis:8-alpine");
        tags.Should().Contain("alpine:latest");
    }

    /// <summary>Inspect reads the configuration the topologies rely on.</summary>
    [Fact]
    public async Task InspectImageAsync_ReadsTheConfiguration()
    {
        //Act
        var detail = await _fixture.Docker.InspectImageAsync("redis:8-alpine",
            TestContext.Current.CancellationToken);

        //Assert
        detail.Os.Should().Be("linux");
        detail.LayerCount.Should().BeGreaterThan(0);
        detail.SizeBytes.Should().BeGreaterThan(0);
        detail.ShortId.Should().NotBeNullOrEmpty();
        //The entrypoint is what loads the bundled modules, so it must be there to be left alone.
        detail.Entrypoint.Count.Should().BeGreaterThan(0);
    }

    /// <summary>History comes back newest first with real instructions.</summary>
    [Fact]
    public async Task GetImageHistoryAsync_ReturnsLayers()
    {
        //Act
        var layers = await _fixture.Docker.GetImageHistoryAsync("alpine:latest",
            TestContext.Current.CancellationToken);

        //Assert
        layers.Count.Should().BeGreaterThan(0);
        layers[0].CreatedBy.Should().NotBeNullOrEmpty();
    }

    /// <summary>A tag can be added and removed again.</summary>
    [Fact]
    public async Task TagImageAsync_AddsATagThatCanBeRemoved()
    {
        //Arrange
        var tag = "redissetup-test/alpine:" + Guid.NewGuid().ToString("N")[..8];
        var token = TestContext.Current.CancellationToken;

        try
        {
            //Act
            await _fixture.Docker.TagImageAsync("alpine:latest", tag, token);
            var tagged = await _fixture.Docker.InspectImageAsync(tag, token);

            await _fixture.Docker.RemoveImageAsync(tag, false, token);
            var act = () => _fixture.Docker.InspectImageAsync(tag, token);

            //Assert
            tagged.RepoTags.Should().Contain(tag);
            var thrown = await act.Should().ThrowAsync<DockerManagementException>();
            thrown.And.IsNotFound.Should().Be(true);
        }
        finally
        {
            DockerCli.TryRun("rmi", tag);
        }
    }

    /// <summary>A pull reports progress lines.</summary>
    [Fact]
    public async Task PullImageAsync_ReportsProgress()
    {
        //Arrange
        var lines = new List<string>();
        var progress = new Progress<string>(lines.Add);

        //Act
        await _fixture.Docker.PullImageAsync("alpine:latest", progress,
            TestContext.Current.CancellationToken);

        //Assert
        //An image that is already local still produces at least one status line.
        lines.Count.Should().BeGreaterThan(0);
    }
}
