using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Topologies;

namespace RedisSetupTool.DockerManagement.Instances;

/// <summary>Finds free host ports for a topology, and remembers what it handed out.</summary>
public interface IHostPortAllocator
{
    /// <summary>Allocates every port the topology needs and reserves them in process.</summary>
    /// <param name="descriptor">The topology being created.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The plan.</returns>
    Task<PortPlan> AllocateAsync(TopologyDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Works out a plan without reserving anything, for the create form's preview.</summary>
    /// <param name="descriptor">The topology being previewed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The plan.</returns>
    Task<PortPlan> PreviewAsync(TopologyDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the soft reservation a plan holds.</summary>
    /// <param name="plan">The plan to release; null is ignored.</param>
    void Release(PortPlan plan);
}
