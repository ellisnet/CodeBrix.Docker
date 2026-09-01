using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RedisSetupTool.DockerManagement.Topologies.Builders;

/// <summary>One strategy per topology shape.</summary>
internal interface ITopologyBuilder
{
    /// <summary>Gets the topologies this builder knows how to create.</summary>
    IReadOnlyList<TopologyId> Supported { get; }

    /// <summary>Gets how many progress steps the build reports.</summary>
    /// <param name="descriptor">The topology being created.</param>
    /// <returns>The step count.</returns>
    int StepCount(TopologyDescriptor descriptor);

    /// <summary>Creates the instance.</summary>
    /// <param name="context">The shared build context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The nodes and the connection details.</returns>
    Task<TopologyBuildResult> BuildAsync(TopologyBuildContext context,
        CancellationToken cancellationToken);
}
