using Microsoft.Extensions.DependencyInjection;
using RedisSetupTool.RedisManagement.Health;
using RedisSetupTool.RedisManagement.Redlock;

namespace RedisSetupTool.RedisManagement;

/// <summary>Registers everything this library offers.</summary>
public static class RegisterServices
{
    /// <summary>Adds the connection factory, the probe, the lock service and the health monitor.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRedisManagement(this IServiceCollection services)
    {
        services.AddSingleton<IRedisConnectionFactory, RedisConnectionFactory>();
        services.AddSingleton<IRedlockService, RedlockService>();
        services.AddSingleton<IRedisProbe, RedisProbe>();
        services.AddSingleton<IRedisHealthMonitor, RedisHealthMonitor>();
        return services;
    }
}
