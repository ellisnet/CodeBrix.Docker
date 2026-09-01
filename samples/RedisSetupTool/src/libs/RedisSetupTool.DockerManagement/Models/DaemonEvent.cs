using System;

namespace RedisSetupTool.DockerManagement.Models;

/// <summary>One line from the daemon's event stream.</summary>
public sealed class DaemonEvent
{
    /// <summary>Gets when the event happened.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Gets the object type, for example <c>container</c>.</summary>
    public string Type { get; init; }

    /// <summary>Gets the action, for example <c>start</c>.</summary>
    public string Action { get; init; }

    /// <summary>Gets the id of the object the event is about.</summary>
    public string ActorId { get; init; }

    /// <summary>Gets the name of the object the event is about, when the daemon supplied one.</summary>
    public string ActorName { get; init; }

    /// <summary>Gets the event scope, for example <c>local</c>.</summary>
    public string Scope { get; init; }

    /// <summary>Gets a one-line rendering suitable for a log pane.</summary>
    public string Line { get; init; }
}
