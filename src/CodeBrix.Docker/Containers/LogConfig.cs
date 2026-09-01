using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Docker;

/// <summary>
/// A container's logging-driver configuration.
/// </summary>
public sealed class LogConfig
{
    /// <summary>Gets or sets the driver name, for example <c>json-file</c>, <c>local</c> or <c>none</c>.</summary>
    [JsonPropertyName("Type")]
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the driver options. For <c>json-file</c>, <c>max-size</c> and <c>max-file</c> are
    /// what keep logs from filling the disk.
    /// </summary>
    [JsonPropertyName("Config")]
    public IDictionary<string, string> Config { get; set; }
}
