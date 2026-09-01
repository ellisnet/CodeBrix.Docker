using System;
using System.Globalization;
using System.Threading.Tasks;
using RedisSetupTool.RedisManagement.Exercises;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>
/// The one class here that talks to a real Redis. Point it at any standalone node with
/// <c>REDISSETUP_TEST_REDIS=host:port[,password]</c>.
/// </summary>
public class RedisProbeLiveTests
{
    /// <summary>The environment variable naming the endpoint.</summary>
    public const string Gate = "REDISSETUP_TEST_REDIS";

    /// <summary>A real node answers a ping and describes itself.</summary>
    [EndpointGatedFact(Gate)]
    public async Task PingAndInfo_AnswerFromARealNode()
    {
        //Arrange
        var probe = new RedisProbe(new RedisConnectionFactory());
        var descriptor = Descriptor();

        //Act
        var ping = await probe.PingAsync(descriptor, TestContext.Current.CancellationToken);
        var info = await probe.GetServerInfoAsync(descriptor, TestContext.Current.CancellationToken);

        //Assert
        ping.Succeeded.Should().Be(true);
        info.Version.Should().NotBeNullOrEmpty();
        info.Mode.Should().NotBeNullOrEmpty();
        info.Role.Should().Be("master");
    }

    /// <summary>A real node survives a round of real work.</summary>
    [EndpointGatedFact(Gate)]
    public async Task ExerciseAsync_RoundTripsAgainstARealNode()
    {
        //Arrange
        var probe = new RedisProbe(new RedisConnectionFactory());
        var options = new RedisExerciseOptions { KeyPrefix = "redissetup:livetest", KeyCount = 10 };

        //Act
        var result = await probe.ExerciseAsync(Descriptor(), options,
            TestContext.Current.CancellationToken);

        //Assert
        result.Succeeded.Should().Be(true);
        result.Steps.Count.Should().Be(5);
    }

    /// <summary>A real standalone node passes its verification checks.</summary>
    [EndpointGatedFact(Gate)]
    public async Task VerifyAsync_PassesAgainstARealStandaloneNode()
    {
        //Arrange
        var probe = new RedisProbe(new RedisConnectionFactory());

        //Act
        var verification = await probe.VerifyAsync(Descriptor(),
            TestContext.Current.CancellationToken);

        //Assert
        verification.Succeeded.Should().Be(true);
    }

    private static RedisConnectionDescriptor Descriptor()
    {
        var setting = Environment.GetEnvironmentVariable(Gate) ?? string.Empty;
        var parts = setting.Split(',', 2);
        var address = parts[0].Split(':', 2);

        return new RedisConnectionDescriptor
        {
            Shape = RedisConnectionShape.Standalone,
            Endpoints =
            [
                new RedisHostPort
                {
                    Host = address[0],
                    Port = address.Length > 1
                        ? int.Parse(address[1], CultureInfo.InvariantCulture)
                        : 6379,
                },
            ],
            Credentials = parts.Length > 1
                ? new RedisCredentials { Password = parts[1] }
                : null,
        };
    }
}
