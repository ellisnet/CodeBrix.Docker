namespace RedisSetupTool.RedisManagement;

/// <summary>What a client authenticates with.</summary>
public sealed class RedisCredentials
{
    /// <summary>Gets the user name; null or <c>default</c> for password-only authentication.</summary>
    public string Username { get; init; }

    /// <summary>Gets the password.</summary>
    public string Password { get; init; }
}
