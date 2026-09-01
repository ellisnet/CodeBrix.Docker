using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

/// <summary>
/// The fixture guarantees cleanup at the end of the run; this guard proves the suite also cleans up
/// as it goes, so no test inherits another test's containers.
/// </summary>
[Collection(DockerTestCollection.Name)]
public sealed class ResourceLeakGuardTests(DockerTestFixture fixture)
{
    [Fact]
    public async Task NoContainersFromEarlierTestsAreStillAround()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Tests run sequentially, so nothing else is creating containers right now; an auto-removing
        // container may still be on its way out, hence the poll.
        var leaked = await Poll.UntilAsync(
            token => fixture.ListOwnContainersAsync(token),
            containers => containers.Count == 0,
            TimeSpan.FromSeconds(30), "every container created by the suite to be removed",
            TimeSpan.FromSeconds(1), cancellation.Token);

        Assert.Empty(leaked.Select(container => container.DisplayName));
    }
}
