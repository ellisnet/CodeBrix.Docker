using Microsoft.Extensions.DependencyInjection;
using RedisSetupTool.DockerManagement;
using RedisSetupTool.RedisManagement;
using RedisSetupTool.Services;

namespace RedisSetupTool;

/// <summary>
/// The application's dependency-injection registrations. <c>App.xaml.cs</c> calls
/// <see cref="AddRedisSetupTool"/> and gets everything: the Docker facade and the topology
/// service from <c>RedisSetupTool.DockerManagement</c>, the Redis client tier from
/// <c>RedisSetupTool.RedisManagement</c>, and the shared snapshot the eight sections read.
/// </summary>
public static class RegisterServices
{
    /// <summary>Registers everything the application resolves.</summary>
    /// <param name="services">The collection to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRedisSetupTool(this IServiceCollection services)
    {
        services.AddDockerManagement();
        services.AddRedisManagement();
        services.AddSingleton<AppState>();
        return services;
    }
}
