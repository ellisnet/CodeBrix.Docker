using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Topologies;

namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>
/// The default allocator. It builds the in-use set from every published port on the daemon plus its
/// own soft reservations, then walks the relevant range binding each candidate to prove it is free.
/// The window between the bind probe and the daemon publishing the port is a genuine race; the
/// soft-reservation set closes the in-process half of it and the create path rolls back the rest.
/// </summary>
public sealed class HostPortAllocator : IHostPortAllocator
{
    private readonly Func<CancellationToken, Task<IReadOnlyCollection<int>>> _inUseProvider;
    private readonly Func<int, bool> _isBindable;
    private readonly HashSet<int> _softReserved = [];
    private readonly object _gate = new();

    /// <summary>Creates the allocator over a live daemon.</summary>
    /// <param name="docker">The facade used to read the ports already published.</param>
    /// <param name="options">The port ranges; null selects the defaults.</param>
    public HostPortAllocator(IDockerManager docker, PortAllocationOptions options = null)
        : this(ct => ReadPublishedPortsAsync(docker, ct), CanBind, options)
    {
        ArgumentNullException.ThrowIfNull(docker);
    }

    internal HostPortAllocator(Func<CancellationToken, Task<IReadOnlyCollection<int>>> inUseProvider,
        Func<int, bool> isBindable, PortAllocationOptions options)
    {
        _inUseProvider = inUseProvider ?? throw new ArgumentNullException(nameof(inUseProvider));
        _isBindable = isBindable ?? CanBind;
        Options = options ?? new PortAllocationOptions();
    }

    /// <summary>Gets the ranges this allocator draws from.</summary>
    public PortAllocationOptions Options { get; }

    /// <inheritdoc />
    public Task<PortPlan> AllocateAsync(TopologyDescriptor descriptor,
        CancellationToken cancellationToken = default) =>
        PlanAsync(descriptor, reserve: true, cancellationToken);

    /// <inheritdoc />
    public Task<PortPlan> PreviewAsync(TopologyDescriptor descriptor,
        CancellationToken cancellationToken = default) =>
        PlanAsync(descriptor, reserve: false, cancellationToken);

    /// <inheritdoc />
    public void Release(PortPlan plan)
    {
        if (plan is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var port in plan.DataPorts)
            {
                _softReserved.Remove(port);
            }

            foreach (var port in plan.SentinelPorts)
            {
                _softReserved.Remove(port);
            }

            foreach (var port in plan.BusPorts)
            {
                _softReserved.Remove(port);
            }
        }
    }

    private async Task<PortPlan> PlanAsync(TopologyDescriptor descriptor, bool reserve,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var published = await _inUseProvider(cancellationToken).ConfigureAwait(false);
        var inUse = new HashSet<int>(published ?? []);

        lock (_gate)
        {
            foreach (var port in _softReserved)
            {
                inUse.Add(port);
            }

            var dataStart = descriptor.NeedsBusPorts
                ? Options.ClusterPortRangeStart
                : Options.DataPortRangeStart;
            var dataEnd = descriptor.NeedsBusPorts
                ? Options.ClusterPortRangeEnd
                : Options.DataPortRangeEnd;

            var dataPorts = new List<int>(descriptor.DataPortCount);
            var busPorts = new List<int>(descriptor.NeedsBusPorts ? descriptor.DataPortCount : 0);
            var sentinelPorts = new List<int>(descriptor.SentinelPortCount);

            for (var taken = 0; taken < descriptor.DataPortCount; taken++)
            {
                var port = Take(inUse, dataStart, dataEnd, descriptor.DataPortCount,
                    descriptor.NeedsBusPorts ? Options.BusPortOffset : 0, "data");
                dataPorts.Add(port);
                if (descriptor.NeedsBusPorts)
                {
                    busPorts.Add(port + Options.BusPortOffset);
                }
            }

            for (var taken = 0; taken < descriptor.SentinelPortCount; taken++)
            {
                sentinelPorts.Add(Take(inUse, Options.SentinelPortRangeStart,
                    Options.SentinelPortRangeEnd, descriptor.SentinelPortCount, 0, "sentinel"));
            }

            if (reserve)
            {
                foreach (var port in dataPorts)
                {
                    _softReserved.Add(port);
                }

                foreach (var port in busPorts)
                {
                    _softReserved.Add(port);
                }

                foreach (var port in sentinelPorts)
                {
                    _softReserved.Add(port);
                }
            }

            return new PortPlan
            {
                DataPorts = dataPorts,
                SentinelPorts = sentinelPorts,
                BusPorts = busPorts,
            };
        }
    }

    private int Take(HashSet<int> inUse, int start, int end, int wanted, int busOffset, string kind)
    {
        for (var candidate = start; candidate <= end; candidate++)
        {
            if (inUse.Contains(candidate))
            {
                continue;
            }

            if (busOffset > 0 && inUse.Contains(candidate + busOffset))
            {
                continue;
            }

            if (!_isBindable(candidate))
            {
                inUse.Add(candidate);
                continue;
            }

            if (busOffset > 0 && !_isBindable(candidate + busOffset))
            {
                inUse.Add(candidate);
                inUse.Add(candidate + busOffset);
                continue;
            }

            inUse.Add(candidate);
            if (busOffset > 0)
            {
                inUse.Add(candidate + busOffset);
            }

            return candidate;
        }

        throw new DockerManagementException(string.Format(CultureInfo.InvariantCulture,
            "No free {0} port is left in the range {1}-{2}; {3} were wanted. " +
            "Destroy an instance, or widen PortAllocationOptions.", kind, start, end, wanted));
    }

    private static async Task<IReadOnlyCollection<int>> ReadPublishedPortsAsync(IDockerManager docker,
        CancellationToken cancellationToken)
    {
        var ports = new HashSet<int>();
        var containers = await docker.ListContainersAsync(includeStopped: true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var container in containers)
        {
            foreach (var mapping in container.Ports)
            {
                if (mapping.HostPort.HasValue)
                {
                    ports.Add(mapping.HostPort.Value);
                }
            }
        }

        return ports;
    }

    private static bool CanBind(int port)
    {
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
