using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The image-level configuration a container was created with, from <c>Config</c> in the inspect payload.
/// </summary>
public sealed class ContainerConfig
{
    /// <summary>Gets the image reference the container was created from.</summary>
    [JsonPropertyName("Image")]
    public string Image { get; init; }

    /// <summary>
    /// Gets the user the container's main process runs as. An empty value means <c>root</c>, which is
    /// worth flagging in a security review.
    /// </summary>
    [JsonPropertyName("User")]
    public string User { get; init; }

    /// <summary>Gets the environment variables, each in <c>KEY=VALUE</c> form.</summary>
    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; }

    /// <summary>Gets the container labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>Gets the command.</summary>
    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; }

    /// <summary>Gets the entrypoint.</summary>
    [JsonPropertyName("Entrypoint")]
    public IReadOnlyList<string> Entrypoint { get; init; }

    /// <summary>Gets the working directory.</summary>
    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; init; }

    /// <summary>Gets the container host name.</summary>
    [JsonPropertyName("Hostname")]
    public string Hostname { get; init; }

    /// <summary>Gets a value indicating whether the container was allocated a pseudo-TTY.</summary>
    [JsonPropertyName("Tty")]
    public bool Tty { get; init; }

    /// <summary>Gets the effective healthcheck, or <see langword="null"/> when the container has none.</summary>
    [JsonPropertyName("Healthcheck")]
    public HealthcheckSpec Healthcheck { get; init; }

    /// <summary>Gets the ports the image or container declares as exposed.</summary>
    [JsonPropertyName("ExposedPorts")]
    public IReadOnlyDictionary<string, JsonEmptyObject> ExposedPorts { get; init; }
}
