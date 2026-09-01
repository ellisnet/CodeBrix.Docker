using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;
using SilverAssertions;
using Xunit;

namespace RedisSetupTool.DockerManagement.Tests;

/// <summary>Covers host port allocation with the daemon and the bind probe both faked.</summary>
public class HostPortAllocatorTests
{
    /// <summary>Ports already published by a container are skipped.</summary>
    [Fact]
    public async Task AllocateAsync_SkipsPortsAlreadyInUse()
    {
        //Arrange
        var allocator = Build([6400, 6401], _ => true);

        //Act
        var plan = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);

        //Assert
        plan.DataPorts.Count.Should().Be(1);
        plan.DataPorts[0].Should().Be(6402);
    }

    /// <summary>A port the bind probe refuses is treated as in use.</summary>
    [Fact]
    public async Task AllocateAsync_SkipsPortsThatWillNotBind()
    {
        //Arrange
        var allocator = Build([], port => port != 6400);

        //Act
        var plan = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);

        //Assert
        plan.DataPorts[0].Should().Be(6401);
    }

    /// <summary>A reservation survives until it is released.</summary>
    [Fact]
    public async Task AllocateAsync_ReservesUntilReleased()
    {
        //Arrange
        var allocator = Build([], _ => true);

        //Act
        var first = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);
        var second = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);
        allocator.Release(first);
        var third = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);

        //Assert
        first.DataPorts[0].Should().Be(6400);
        second.DataPorts[0].Should().Be(6401);
        third.DataPorts[0].Should().Be(6400);
    }

    /// <summary>A preview reserves nothing.</summary>
    [Fact]
    public async Task PreviewAsync_ReservesNothing()
    {
        //Arrange
        var allocator = Build([], _ => true);

        //Act
        var preview = await allocator.PreviewAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);
        var allocated = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);

        //Assert
        preview.DataPorts[0].Should().Be(6400);
        allocated.DataPorts[0].Should().Be(6400);
    }

    /// <summary>Cluster ports come with a bus port ten thousand above them.</summary>
    [Fact]
    public async Task AllocateAsync_ForCluster_PairsEachDataPortWithABusPort()
    {
        //Arrange
        var allocator = Build([], _ => true);

        //Act
        var plan = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.D2),
            TestContext.Current.CancellationToken);

        //Assert
        plan.DataPorts.Count.Should().Be(6);
        plan.BusPorts.Count.Should().Be(6);
        plan.DataPorts[0].Should().Be(7400);
        plan.BusPorts[0].Should().Be(17400);
        plan.BusPorts[5].Should().Be(plan.DataPorts[5] + 10000);
    }

    /// <summary>A data port whose bus port is taken is skipped as a pair.</summary>
    [Fact]
    public async Task AllocateAsync_ForCluster_SkipsAPortWhoseBusPortIsTaken()
    {
        //Arrange
        var allocator = Build([17400], _ => true);

        //Act
        var plan = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.D2),
            TestContext.Current.CancellationToken);

        //Assert
        plan.DataPorts[0].Should().Be(7401);
        plan.BusPorts[0].Should().Be(17401);
    }

    /// <summary>Sentinel ports come out of their own range.</summary>
    [Fact]
    public async Task AllocateAsync_ForSentinel_AllocatesFromBothRanges()
    {
        //Arrange
        var allocator = Build([], _ => true);

        //Act
        var plan = await allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.C1),
            TestContext.Current.CancellationToken);

        //Assert
        plan.DataPorts.Count.Should().Be(3);
        plan.SentinelPorts.Count.Should().Be(3);
        plan.DataPorts[0].Should().Be(6400);
        plan.SentinelPorts[0].Should().Be(26400);
        plan.Describe().Should().Contain("data 6400, 6401, 6402");
        plan.Describe().Should().Contain("sentinel 26400");
    }

    /// <summary>An exhausted range says which range and how many were wanted.</summary>
    [Fact]
    public async Task AllocateAsync_WhenTheRangeIsExhausted_ThrowsWithALegibleMessage()
    {
        //Arrange
        var options = new PortAllocationOptions { DataPortRangeStart = 6400, DataPortRangeEnd = 6400 };
        var allocator = new HostPortAllocator(_ => Task.FromResult<IReadOnlyCollection<int>>([6400]),
            _ => true, options);

        //Act
        var act = () => allocator.AllocateAsync(TopologyCatalog.Get(TopologyId.A1),
            TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<DockerManagementException>();
        thrown.And.Message.Should().Contain("6400-6400");
        thrown.And.Message.Should().Contain("data");
    }

    /// <summary>Concurrent allocations never hand out the same port twice.</summary>
    [Fact]
    public async Task AllocateAsync_WhenCalledConcurrently_NeverOverlaps()
    {
        //Arrange
        var allocator = Build([], _ => true);
        var descriptor = TopologyCatalog.Get(TopologyId.H1);

        //Act
        var tasks = new Task<PortPlan>[4];
        for (var index = 0; index < tasks.Length; index++)
        {
            tasks[index] = allocator.AllocateAsync(descriptor, TestContext.Current.CancellationToken);
        }

        var plans = await Task.WhenAll(tasks);

        //Assert
        var seen = new HashSet<int>();
        foreach (var plan in plans)
        {
            foreach (var port in plan.DataPorts)
            {
                seen.Add(port).Should().Be(true);
            }
        }

        seen.Count.Should().Be(20);
    }

    private static HostPortAllocator Build(IReadOnlyCollection<int> inUse, Func<int, bool> bindable) =>
        new(_ => Task.FromResult(inUse), bindable, new PortAllocationOptions());
}
