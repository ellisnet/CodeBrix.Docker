namespace CodeBrix.Docker;

/// <summary>
/// A container's captured output, already demultiplexed into the two streams.
/// </summary>
/// <param name="Stdout">Everything the container wrote to standard output.</param>
/// <param name="Stderr">Everything the container wrote to standard error.</param>
public sealed record ContainerLogs(string Stdout, string Stderr)
{
    /// <summary>Gets a value indicating whether both streams are empty.</summary>
    public bool IsEmpty => Stdout.Length == 0 && Stderr.Length == 0;

    /// <summary>Gets both streams concatenated, standard output first.</summary>
    public string Combined => Stdout + Stderr;
}
