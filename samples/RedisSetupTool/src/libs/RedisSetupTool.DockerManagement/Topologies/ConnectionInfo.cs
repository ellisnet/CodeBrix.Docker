using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>Everything needed to connect to an instance, ready to paste.</summary>
public sealed class ConnectionInfo
{
    /// <summary>Gets how a client connects.</summary>
    public ConnectionShape Shape { get; init; }

    /// <summary>Gets the endpoints; never null.</summary>
    public IReadOnlyList<RedisEndpoint> Endpoints { get; init; } = [];

    /// <summary>Gets the user name, normally <c>default</c>.</summary>
    public string Username { get; init; }

    /// <summary>Gets the password, when the topology has one.</summary>
    public string Password { get; init; }

    /// <summary>Gets the sentinel master name, for the sentinel shape.</summary>
    public string ServiceName { get; init; }

    /// <summary>Gets the extra ACL users; never null.</summary>
    public IReadOnlyList<RedisUser> AdditionalUsers { get; init; } = [];

    /// <summary>Gets the paste-ready client configuration string.</summary>
    public string ConnectionString { get; init; }

    /// <summary>Gets a paste-ready command-line client invocation.</summary>
    public string CliCommand { get; init; }

    /// <summary>Gets the notes shown beside the connection details; never null.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
