namespace RedisSetupTool.DockerManagement.Models;

/// <summary>A container's captured output.</summary>
public sealed class ContainerLogText
{
    /// <summary>Gets the standard output text.</summary>
    public string Stdout { get; init; } = string.Empty;

    /// <summary>Gets the standard error text.</summary>
    public string Stderr { get; init; } = string.Empty;

    /// <summary>Gets both streams, standard output first.</summary>
    public string Combined => Stdout + Stderr;

    /// <summary>Gets a value indicating whether both streams are empty.</summary>
    public bool IsEmpty => Stdout.Length == 0 && Stderr.Length == 0;
}
