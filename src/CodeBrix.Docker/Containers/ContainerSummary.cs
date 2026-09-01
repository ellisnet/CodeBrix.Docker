using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A container as returned by <c>GET /containers/json</c> — the shape behind <c>docker ps</c>.
/// </summary>
public sealed class ContainerSummary
{
    /// <summary>Gets the full container id.</summary>
    [JsonPropertyName("Id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the container names, each with the daemon's leading slash.</summary>
    [JsonPropertyName("Names")]
    public IReadOnlyList<string> Names { get; init; }

    /// <summary>Gets the image reference the container was created from.</summary>
    [JsonPropertyName("Image")]
    public string Image { get; init; }

    /// <summary>Gets the image id the container is actually running.</summary>
    [JsonPropertyName("ImageID")]
    public string ImageId { get; init; }

    /// <summary>Gets the command line the container runs.</summary>
    [JsonPropertyName("Command")]
    public string Command { get; init; }

    /// <summary>Gets the creation time.</summary>
    [JsonPropertyName("Created")]
    public long CreatedUnixSeconds { get; init; }

    /// <summary>Gets the lifecycle state, for example <c>running</c>, <c>exited</c> or <c>created</c>.</summary>
    [JsonPropertyName("State")]
    public string State { get; init; }

    /// <summary>Gets the human-readable status, for example <c>Up 3 minutes</c>.</summary>
    [JsonPropertyName("Status")]
    public string Status { get; init; }

    /// <summary>Gets the container labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>Gets the exposed and published ports.</summary>
    [JsonPropertyName("Ports")]
    public IReadOnlyList<ContainerPort> Ports { get; init; }

    /// <summary>Gets the writable-layer size in bytes, populated only when sizes were requested.</summary>
    [JsonPropertyName("SizeRw")]
    public long SizeRw { get; init; }

    /// <summary>Gets the total filesystem size in bytes, populated only when sizes were requested.</summary>
    [JsonPropertyName("SizeRootFs")]
    public long SizeRootFs { get; init; }

    /// <summary>Gets the first name without the daemon's leading slash, or the short id as a fallback.</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var name = Names is { Count: > 0 } ? Names[0] : null;
            if (!string.IsNullOrEmpty(name))
            {
                return name.TrimStart('/');
            }

            return Id.Length >= 12 ? Id[..12] : Id;
        }
    }

    /// <summary>Gets the creation time, or <see langword="null"/> when the daemon reported none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Created =>
        CreatedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(CreatedUnixSeconds) : null;

    /// <summary>Gets a value indicating whether the container is currently running.</summary>
    [JsonIgnore]
    public bool IsRunning => string.Equals(State, "running", StringComparison.OrdinalIgnoreCase);
}
