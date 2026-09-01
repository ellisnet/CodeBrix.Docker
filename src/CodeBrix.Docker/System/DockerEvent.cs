using System;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A single entry from the daemon event stream (<c>GET /events</c>).
/// </summary>
public sealed class DockerEvent
{
    /// <summary>Gets the object type the event concerns, for example <c>container</c> or <c>image</c>.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; init; }

    /// <summary>Gets the action, for example <c>start</c>, <c>die</c>, <c>oom</c> or <c>health_status: healthy</c>.</summary>
    [JsonPropertyName("Action")]
    public string Action { get; init; }

    /// <summary>Gets the object the event concerns.</summary>
    [JsonPropertyName("Actor")]
    public DockerEventActor Actor { get; init; }

    /// <summary>Gets the event scope, <c>local</c> or <c>swarm</c>.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; }

    /// <summary>Gets the legacy status field (older API shape).</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; }

    /// <summary>Gets the legacy identifier field (older API shape).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    /// <summary>Gets the legacy source image field (older API shape).</summary>
    [JsonPropertyName("from")]
    public string From { get; init; }

    /// <summary>Gets the event time in Unix seconds.</summary>
    [JsonPropertyName("time")]
    public long Time { get; init; }

    /// <summary>Gets the event time in Unix nanoseconds.</summary>
    [JsonPropertyName("timeNano")]
    public long TimeNano { get; init; }

    /// <summary>Gets the event time as a timestamp, or <see langword="null"/> when the daemon sent none.</summary>
    [JsonIgnore]
    public DateTimeOffset? Timestamp => TimeNano > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(TimeNano / 1_000_000)
        : Time > 0
            ? DateTimeOffset.FromUnixTimeSeconds(Time)
            : null;
}
