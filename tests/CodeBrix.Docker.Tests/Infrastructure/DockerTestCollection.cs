using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// Every test class joins this one collection: the suite drives a live daemon, so its tests must run
/// sequentially and share a single client and a single cleanup sweep.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DockerTestCollection : ICollectionFixture<DockerTestFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "docker-daemon";
}
