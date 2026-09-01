namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One ACL user declared by a topology.</summary>
public sealed class RedisUser
{
    /// <summary>Gets the user name.</summary>
    public string Username { get; init; }

    /// <summary>Gets the password.</summary>
    public string Password { get; init; }

    /// <summary>Gets the permission tokens, for example <c>~app:* +@read +@write</c>.</summary>
    public string Permissions { get; init; }
}
