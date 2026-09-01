using System.Collections.Generic;

namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>One thing the user can set when creating an instance.</summary>
public sealed class TopologyParameter
{
    /// <summary>The token a default value uses to ask for a generated password.</summary>
    public const string GeneratedToken = "{generated}";

    /// <summary>Gets the parameter key, used in <see cref="TopologyRequest.Parameters"/>.</summary>
    public string Key { get; init; }

    /// <summary>Gets the form label.</summary>
    public string Label { get; init; }

    /// <summary>Gets the editor to show.</summary>
    public TopologyParameterKind Kind { get; init; }

    /// <summary>Gets the default value, which may be <see cref="GeneratedToken"/>.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>Gets the allowed values for a <see cref="TopologyParameterKind.Choice"/>; never null.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];

    /// <summary>Gets the help text shown under the editor.</summary>
    public string HelpText { get; init; }

    /// <summary>Gets a value indicating whether a value must be supplied.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets the smallest allowed value, for an integer parameter.</summary>
    public long? Minimum { get; init; }

    /// <summary>Gets the largest allowed value, for an integer parameter.</summary>
    public long? Maximum { get; init; }
}
