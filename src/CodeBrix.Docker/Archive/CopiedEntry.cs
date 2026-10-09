using System.IO;

namespace CodeBrix.Docker;

/// <summary>
/// One file system entry written to the host by
/// <see cref="ContainerOperations.CopyFromContainerToDirectoryAsync"/>.
/// </summary>
/// <param name="ContainerPath">The entry's absolute path inside the container.</param>
/// <param name="HostPath">The full path it was written to on the host.</param>
/// <param name="Size">The content size in bytes; zero for directories and links.</param>
/// <param name="Mode">
/// The permission bits the archive carried. Recorded on every host, including Windows, where they
/// cannot be applied.
/// </param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="IsExecutable">Whether the entry is a file with at least one execute bit in <paramref name="Mode"/>.</param>
public sealed record CopiedEntry(string ContainerPath, string HostPath, long Size, UnixFileMode Mode,
    bool IsDirectory, bool IsExecutable);
