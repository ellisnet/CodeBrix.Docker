namespace RedisSetupTool.DockerManagement.Topologies;

/// <summary>What editor the create form shows for a parameter.</summary>
public enum TopologyParameterKind
{
    /// <summary>A single-line text box.</summary>
    Text,

    /// <summary>A password box with a generate button.</summary>
    Password,

    /// <summary>A number box.</summary>
    Integer,

    /// <summary>A combo box over <see cref="TopologyParameter.Choices"/>.</summary>
    Choice,

    /// <summary>A toggle switch.</summary>
    Boolean,

    /// <summary>A multi-line text box.</summary>
    MultiLineText,
}
