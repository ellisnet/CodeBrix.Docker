using System.Collections.Generic;

namespace CodeBrix.Docker;

/// <summary>
/// What <see cref="ContainerOperations.CopyFromContainerToDirectoryAsync"/> wrote to the host.
/// </summary>
public sealed class CopyFromContainerResult
{
    /// <summary>Gets every entry written, in archive order.</summary>
    public IReadOnlyList<CopiedEntry> Entries { get; internal init; } = [];

    /// <summary>Gets the total file content bytes written.</summary>
    public long TotalBytes { get; internal init; }

    /// <summary>Gets the number of regular files written (hard links count, as they are written as copies).</summary>
    public int FileCount { get; internal init; }

    /// <summary>Gets the number of directories created or reused.</summary>
    public int DirectoryCount { get; internal init; }

    /// <summary>
    /// Gets the archive names of entries that were deliberately not written: symbolic links (skipped by
    /// default, or pointing outside the destination), device and pipe nodes, and existing files left
    /// alone because <see cref="CopyFromContainerOptions.Overwrite"/> was off.
    /// </summary>
    public IReadOnlyList<string> SkippedEntries { get; internal init; } = [];
}
