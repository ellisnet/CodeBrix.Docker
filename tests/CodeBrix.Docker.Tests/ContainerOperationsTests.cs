using System;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Daemon-free tests for how <see cref="ContainerOperations"/> shapes the <c>POST /containers/create</c>
/// request path: the <c>name</c> and <c>platform</c> query parameters.
/// </summary>
public sealed class ContainerOperationsTests
{
    [Fact]
    public void CreatePath_WithoutANameOrAPlatform_HasNoQueryString()
    {
        //Arrange
        var spec = new ContainerSpec { Image = "alpine:latest" };

        //Act
        var path = ContainerOperations.CreatePath(spec);

        //Assert
        path.Should().Be("containers/create");
    }

    [Fact]
    public void CreatePath_WithAName_SendsTheNameOnly()
    {
        //Arrange
        var spec = new ContainerSpec { Image = "alpine:latest", Name = "codebrix-test-a" };

        //Act
        var path = ContainerOperations.CreatePath(spec);

        //Assert
        path.Should().Be("containers/create?name=codebrix-test-a");
    }

    [Fact]
    public void CreatePath_WithAPlatform_SendsThePlatformParameter()
    {
        //Arrange
        var spec = new ContainerSpec { Image = "alpine:latest", Platform = "linux/arm64" };

        //Act
        var path = ContainerOperations.CreatePath(spec);

        //Assert
        path.Should().Be("containers/create?platform=linux%2Farm64");
    }

    [Fact]
    public void CreatePath_WithANameAndAPlatform_SendsBoth()
    {
        //Arrange
        var spec = new ContainerSpec
        {
            Image = "alpine:latest",
            Name = "codebrix-test-b",
            Platform = " linux/riscv64 ",
        };

        //Act
        var path = ContainerOperations.CreatePath(spec);

        //Assert
        path.Should().Be("containers/create?name=codebrix-test-b&platform=linux%2Friscv64");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void CreatePath_WithABlankPlatform_SendsNoPlatformParameter(string platform)
    {
        //Arrange
        var spec = new ContainerSpec { Image = "alpine:latest", Platform = platform };

        //Act
        var path = ContainerOperations.CreatePath(spec);

        //Assert
        path.Should().Be("containers/create");
    }

    [Fact]
    public async Task CreateAsync_WithAPlatformList_ThrowsBeforeAnyRequest()
    {
        //Arrange
        // No API client: reaching the daemon would be a NullReferenceException, not an ArgumentException.
        var containers = new ContainerOperations(api: null);
        var spec = new ContainerSpec { Image = "alpine:latest", Platform = "linux/amd64,linux/arm64" };

        //Act
        Func<Task> create = () => containers.CreateAsync(spec, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await create.Should().ThrowAsync<ArgumentException>();
        thrown.Which.Message.Should().Contain("ContainerSpec.Platform");
    }
}
