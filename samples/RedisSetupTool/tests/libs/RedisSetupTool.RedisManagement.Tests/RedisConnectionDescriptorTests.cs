using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>Covers the descriptor value types.</summary>
public class RedisConnectionDescriptorTests
{
    /// <summary>An address renders host and port.</summary>
    [Fact]
    public void ToString_RendersHostAndPort()
    {
        //Arrange
        var endpoint = new RedisHostPort { Host = "127.0.0.1", Port = 6401 };

        //Assert
        endpoint.ToString().Should().Be("127.0.0.1:6401");
    }

    /// <summary>The order endpoints were given in is the order they are kept in.</summary>
    [Fact]
    public void Endpoints_PreserveTheirOrder()
    {
        //Arrange
        var descriptor = new RedisConnectionDescriptor
        {
            Endpoints =
            [
                new RedisHostPort { Host = "a", Port = 1 },
                new RedisHostPort { Host = "b", Port = 2 },
                new RedisHostPort { Host = "c", Port = 3 },
            ],
        };

        //Assert
        descriptor.Endpoints[0].Host.Should().Be("a");
        descriptor.Endpoints[2].Host.Should().Be("c");
    }

    /// <summary>A descriptor with no credentials is legal and produces no password.</summary>
    [Fact]
    public void Credentials_MayBeAbsent()
    {
        //Arrange
        var descriptor = new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints = [new RedisHostPort { Host = "127.0.0.1", Port = 6401 }],
        };

        //Act
        var text = RedisConnectionStringBuilder.Build(descriptor);

        //Assert
        descriptor.Credentials.Should().BeNull();
        text.Should().Be("127.0.0.1:6401,allowAdmin=True,abortConnect=False");
    }

    /// <summary>The collections default to empty rather than null.</summary>
    [Fact]
    public void Collections_DefaultToEmpty()
    {
        //Arrange
        var descriptor = new RedisConnectionDescriptor();

        //Assert
        descriptor.Endpoints.Count.Should().Be(0);
        descriptor.ExpectedModules.Count.Should().Be(0);
        descriptor.ExpectedUsers.Count.Should().Be(0);
        descriptor.ExpectedConfig.Count.Should().Be(0);
    }
}
