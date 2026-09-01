using Microsoft.Extensions.DependencyInjection;
using RedisSetupTool.DockerManagement.Instances;
using RedisSetupTool.DockerManagement.Topologies;

namespace RedisSetupTool.DockerManagement;

/// <summary>Registers everything this library offers.</summary>
public static class RegisterServices
{
    /// <summary>Adds the Docker facade, the port allocator and the topology service as singletons.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDockerManagement(this IServiceCollection services)
    {
        services.AddSingleton<DockerManager>();
        services.AddSingleton<IDockerManager>(provider => provider.GetRequiredService<DockerManager>());
        services.AddSingleton<IHostPortAllocator>(provider =>
            new HostPortAllocator(provider.GetRequiredService<IDockerManager>()));
        services.AddSingleton<IRedisTopologyService, RedisTopologyService>();
        return services;
    }
}
