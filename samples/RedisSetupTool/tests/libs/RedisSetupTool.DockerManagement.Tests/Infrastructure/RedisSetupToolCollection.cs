using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>
/// Every test class joins this one collection: the suite drives a live daemon, so its tests run
/// sequentially and share one facade and one cleanup sweep.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisSetupToolCollection : ICollectionFixture<RedisSetupToolFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "redissetup-daemon";
}
