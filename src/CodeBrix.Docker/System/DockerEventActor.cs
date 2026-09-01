using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The object a <see cref="DockerEvent"/> refers to.
/// </summary>
public sealed class DockerEventActor
{
    /// <summary>Gets the object identifier, for example a container id.</summary>
    [JsonPropertyName("ID")]
    public string Id { get; init; }

    /// <summary>
    /// Gets the object's attributes at the time of the event — labels plus event-specific keys such as
    /// <c>name</c>, <c>image</c> and <c>exitCode</c>.
    /// </summary>
    [JsonPropertyName("Attributes")]
    public IReadOnlyDictionary<string, string> Attributes { get; init; }
}
