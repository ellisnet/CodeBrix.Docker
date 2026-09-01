using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// The configuration baked into an image, from <c>Config</c> in the image inspect payload.
/// </summary>
public sealed class ImageConfig
{
    /// <summary>
    /// Gets the user the image runs as. An empty value means <c>root</c>, which is worth flagging in
    /// a security review.
    /// </summary>
    [JsonPropertyName("User")]
    public string User { get; init; }

    /// <summary>Gets the environment variables, each in <c>KEY=VALUE</c> form.</summary>
    [JsonPropertyName("Env")]
    public IReadOnlyList<string> Env { get; init; }

    /// <summary>Gets the image's default command.</summary>
    [JsonPropertyName("Cmd")]
    public IReadOnlyList<string> Cmd { get; init; }

    /// <summary>Gets the image's entrypoint.</summary>
    [JsonPropertyName("Entrypoint")]
    public IReadOnlyList<string> Entrypoint { get; init; }

    /// <summary>Gets the image's default working directory.</summary>
    [JsonPropertyName("WorkingDir")]
    public string WorkingDir { get; init; }

    /// <summary>Gets the ports the image declares as exposed.</summary>
    [JsonPropertyName("ExposedPorts")]
    public IReadOnlyDictionary<string, JsonEmptyObject> ExposedPorts { get; init; }

    /// <summary>Gets the image labels.</summary>
    [JsonPropertyName("Labels")]
    public IReadOnlyDictionary<string, string> Labels { get; init; }

    /// <summary>
    /// Gets the image's <c>HEALTHCHECK</c>, or <see langword="null"/> when the image declares none.
    /// </summary>
    [JsonPropertyName("Healthcheck")]
    public HealthcheckSpec Healthcheck { get; init; }
}
