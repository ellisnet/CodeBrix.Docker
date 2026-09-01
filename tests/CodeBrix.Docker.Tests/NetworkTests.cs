using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Docker.Tests;

[Collection(DockerTestCollection.Name)]
public sealed class NetworkTests(DockerTestFixture fixture)
{
    private DockerClient Client => fixture.Client;

    [Fact]
    public async Task ContainersOnAUserDefinedNetwork_ResolveEachOtherByNameAndByAlias()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var networkName = fixture.NewName("net");
        string networkId = null;
        string firstId = null;
        string secondId = null;

        try
        {
            networkId = await Client.Networks.CreateAsync(networkName, "bridge",
                DockerTestFixture.TestLabelFilter, cancellation.Token);
            Assert.False(string.IsNullOrWhiteSpace(networkId));

            var first = fixture.Spec("neta", "busybox:latest", "sleep", "300");
            first.NetworkName = networkName;
            first.NetworkAliases.Add("alpha");
            firstId = await Client.Containers.RunAsync(first, cancellation.Token);

            var second = fixture.Spec("netb", "busybox:latest", "sleep", "300");
            second.NetworkName = networkName;
            second.NetworkAliases.Add("beta");
            secondId = await Client.Containers.RunAsync(second, cancellation.Token);

            var firstAddress = await AddressOnNetworkAsync(firstId, networkName, cancellation.Token);
            var secondAddress = await AddressOnNetworkAsync(secondId, networkName, cancellation.Token);

            Assert.False(string.IsNullOrWhiteSpace(firstAddress));
            Assert.False(string.IsNullOrWhiteSpace(secondAddress));

            await AssertResolvesAsync(secondId, first.Name, firstAddress, cancellation.Token);
            await AssertResolvesAsync(secondId, "alpha", firstAddress, cancellation.Token);
            await AssertResolvesAsync(firstId, "beta", secondAddress, cancellation.Token);

            var ping = await Client.Containers.ExecAsync(secondId, ["ping", "-c", "1", "-W", "2", "alpha"],
                cancellationToken: cancellation.Token);
            Assert.Equal(0, ping.ExitCode);
            Assert.Contains(firstAddress, ping.Stdout, StringComparison.Ordinal);

            var inspect = await Client.Networks.InspectAsync(networkName, cancellation.Token);
            Assert.Equal(networkName, inspect.Name);
            Assert.Equal("bridge", inspect.Driver);
            Assert.Equal(DockerTestFixture.LabelValue, inspect.Labels?[DockerTestFixture.LabelName]);
            Assert.Equal(2, inspect.AttachedContainerCount);
            Assert.NotNull(inspect.Containers);
            Assert.True(inspect.Containers.ContainsKey(firstId));
            Assert.True(inspect.Containers.ContainsKey(secondId));

            var listed = await Client.Networks.ListAsync(DockerTestFixture.TestLabelFilter, cancellation.Token);
            Assert.Contains(listed, network => network.Name == networkName);
            Assert.All(listed, network =>
                Assert.Equal(DockerTestFixture.LabelValue, network.Labels?[DockerTestFixture.LabelName]));
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(firstId);
            await fixture.RemoveContainerQuietlyAsync(secondId);
            await fixture.RemoveNetworkQuietlyAsync(networkId);
        }
    }

    [Fact]
    public async Task ConnectAsync_AttachesAContainerWithAnAliasAndDisconnectAsyncDetachesIt()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var networkName = fixture.NewName("net");
        string networkId = null;
        string residentId = null;
        string joinerId = null;

        try
        {
            networkId = await Client.Networks.CreateAsync(networkName, "bridge",
                DockerTestFixture.TestLabelFilter, cancellation.Token);

            var resident = fixture.Spec("netresident", "busybox:latest", "sleep", "300");
            resident.NetworkName = networkName;
            residentId = await Client.Containers.RunAsync(resident, cancellation.Token);

            // The joiner starts on the default bridge and is attached afterwards.
            var joiner = fixture.Spec("netjoiner", "busybox:latest", "sleep", "300");
            joinerId = await Client.Containers.RunAsync(joiner, cancellation.Token);

            var beforeAttach = await Client.Networks.InspectAsync(networkName, cancellation.Token);
            Assert.False(beforeAttach.Containers?.ContainsKey(joinerId) ?? false);

            await Client.Networks.ConnectAsync(networkName, joinerId, ["gamma"], cancellation.Token);

            var joinerAddress = await AddressOnNetworkAsync(joinerId, networkName, cancellation.Token);
            Assert.False(string.IsNullOrWhiteSpace(joinerAddress));
            await AssertResolvesAsync(residentId, "gamma", joinerAddress, cancellation.Token);

            var attached = await Client.Networks.InspectAsync(networkName, cancellation.Token);
            Assert.True(attached.Containers.ContainsKey(joinerId));
            Assert.Equal(2, attached.AttachedContainerCount);

            await Client.Networks.DisconnectAsync(networkName, joinerId, force: false, cancellation.Token);

            var detached = await Client.Networks.InspectAsync(networkName, cancellation.Token);
            Assert.False(detached.Containers?.ContainsKey(joinerId) ?? false);
            Assert.Equal(1, detached.AttachedContainerCount);

            var inspectJoiner = await Client.Containers.InspectAsync(joinerId, cancellation.Token);
            Assert.False(inspectJoiner.NetworkSettings?.Networks?.ContainsKey(networkName) ?? false);

            var lookup = await Client.Containers.ExecAsync(residentId, ["nslookup", "gamma"],
                cancellationToken: cancellation.Token);
            Assert.DoesNotContain(joinerAddress, lookup.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.RemoveContainerQuietlyAsync(residentId);
            await fixture.RemoveContainerQuietlyAsync(joinerId);
            await fixture.RemoveNetworkQuietlyAsync(networkId);
        }
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheNetwork()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var networkName = fixture.NewName("net");
        string networkId = null;

        try
        {
            networkId = await Client.Networks.CreateAsync(networkName, "bridge",
                DockerTestFixture.TestLabelFilter, cancellation.Token);

            await Client.Networks.RemoveAsync(networkId, cancellation.Token);
            var removedId = networkId;
            networkId = null;

            var listed = await Client.Networks.ListAsync(DockerTestFixture.TestLabelFilter, cancellation.Token);
            Assert.DoesNotContain(listed, network => network.Id == removedId);

            await Assert.ThrowsAnyAsync<DockerApiException>(
                () => Client.Networks.InspectAsync(removedId, cancellation.Token));
        }
        finally
        {
            await fixture.RemoveNetworkQuietlyAsync(networkId);
        }
    }

    private async Task<string> AddressOnNetworkAsync(string containerId, string networkName,
        CancellationToken cancellationToken)
    {
        var inspect = await Client.Containers.InspectAsync(containerId, cancellationToken);
        var endpoint = inspect.NetworkSettings?.Networks?[networkName];
        return endpoint?.IpAddress ?? string.Empty;
    }

    private async Task AssertResolvesAsync(string fromContainerId, string hostname, string expectedAddress,
        CancellationToken cancellationToken)
    {
        var result = await Poll.UntilAsync(
            token => Client.Containers.ExecAsync(fromContainerId, ["nslookup", hostname],
                cancellationToken: token),
            lookup => lookup.Stdout.Contains(expectedAddress, StringComparison.Ordinal),
            TimeSpan.FromSeconds(20), $"'{hostname}' to resolve to {expectedAddress}",
            TimeSpan.FromMilliseconds(500), cancellationToken);

        Assert.Contains(expectedAddress, result.Stdout, StringComparison.Ordinal);
    }
}
