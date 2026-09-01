using System.Collections.Generic;

namespace RedisSetupTool.RedisManagement;

/// <summary>Everything needed to open a connection. The Docker side maps onto this.</summary>
public sealed class RedisConnectionDescriptor
{
    /// <summary>Gets the shape of the deployment.</summary>
    public RedisConnectionShape Shape { get; init; }

    /// <summary>Gets the endpoints, in the order they should be tried; never null.</summary>
    public IReadOnlyList<RedisHostPort> Endpoints { get; init; } = [];

    /// <summary>Gets the credentials, when the deployment needs them.</summary>
    public RedisCredentials Credentials { get; init; }

    /// <summary>Gets the sentinel master name, for the sentinel shape.</summary>
    public string ServiceName { get; init; }

    /// <summary>Gets a value indicating whether administrative commands are allowed.</summary>
    public bool AllowAdmin { get; init; } = true;

    /// <summary>Gets how long to wait for a connection, in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 5000;

    /// <summary>Gets how long to wait for a command, in milliseconds.</summary>
    public int SyncTimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Gets configuration settings the deployment is expected to report, keyed by setting name.
    /// Verification checks each one. This is how a preset's point - persistence on, a particular
    /// eviction policy - is proven without this library knowing anything about topologies.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExpectedConfig { get; init; } =
        new Dictionary<string, string>(System.StringComparer.Ordinal);

    /// <summary>Gets the modules the deployment is expected to have loaded; never null.</summary>
    public IReadOnlyList<string> ExpectedModules { get; init; } = [];

    /// <summary>Gets the ACL user names the deployment is expected to declare; never null.</summary>
    public IReadOnlyList<string> ExpectedUsers { get; init; } = [];

    /// <summary>Gets the version prefix the deployment is expected to report, for example <c>6.2</c>.</summary>
    public string ExpectedVersionPrefix { get; init; }
}
