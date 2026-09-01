using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The restart policy as the daemon reports it in <c>HostConfig.RestartPolicy</c>.
/// </summary>
public sealed class HostRestartPolicy
{
    /// <summary>
    /// Gets or sets the policy name: <c>""</c>, <c>no</c>, <c>always</c>, <c>on-failure</c> or
    /// <c>unless-stopped</c>.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    /// <summary>Gets or sets the retry cap, honoured only by <c>on-failure</c>.</summary>
    [JsonPropertyName("MaximumRetryCount")]
    public long MaximumRetryCount { get; set; }

    /// <summary>Gets <see cref="Name"/> as a <see cref="RestartPolicyKind"/>.</summary>
    [JsonIgnore]
    public RestartPolicyKind Kind => Name switch
    {
        "always" => RestartPolicyKind.Always,
        "on-failure" => RestartPolicyKind.OnFailure,
        "unless-stopped" => RestartPolicyKind.UnlessStopped,
        _ => RestartPolicyKind.No,
    };
}
