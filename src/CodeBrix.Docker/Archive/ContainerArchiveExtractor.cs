using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Extracts a tar stream produced by the Engine API's <c>GET /containers/{id}/archive</c> into a host
/// folder, refusing any entry that would land outside it.
/// </summary>
internal static class ContainerArchiveExtractor
{
    private const UnixFileMode AnyExecute =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Extracts every entry of <paramref name="tarStream"/> beneath <paramref name="hostDirectory"/>.
    /// </summary>
    /// <param name="tarStream">The archive. It is read forward only and not disposed.</param>
    /// <param name="containerPath">The container path the archive was taken from, for reporting.</param>
    /// <param name="hostDirectory">The destination folder; created when missing.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was written and what was skipped.</returns>
    /// <exception cref="DockerException">An entry's path resolves outside <paramref name="hostDirectory"/>.</exception>
    public static async Task<CopyFromContainerResult> ExtractAsync(Stream tarStream, string containerPath,
        string hostDirectory, CopyFromContainerOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tarStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);
        options ??= new CopyFromContainerOptions();

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostDirectory));
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        var containerParent = ContainerParent(containerPath);
        var entries = new List<CopiedEntry>();
        var skipped = new List<string>();
        var directories = new List<(string Path, UnixFileMode Mode, DateTimeOffset Modified)>();
        long totalBytes = 0;
        var fileCount = 0;
        var directoryCount = 0;
        string archiveRoot = null;
        var stripping = false;

        await using var reader = new TarReader(tarStream, leaveOpen: true);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false) is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType is TarEntryType.GlobalExtendedAttributes)
            {
                continue;
            }

            var name = NormalizeName(entry.Name);
            if (name.Length == 0)
            {
                continue;
            }

            if (archiveRoot is null)
            {
                archiveRoot = FirstSegment(name);
                stripping = options.StripRootFolder && entry.EntryType == TarEntryType.Directory
                            && name.IndexOf('/') < 0;
            }

            var relative = name;
            if (stripping)
            {
                if (string.Equals(name, archiveRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith(archiveRoot + "/", StringComparison.Ordinal))
                {
                    relative = name[(archiveRoot.Length + 1)..];
                }
            }

            var hostPath = Resolve(root, rootWithSeparator, relative, entry.Name);
            EnsureNoLinkedAncestorEscapes(root, rootWithSeparator, hostPath, entry.Name);

            var containerEntryPath = containerParent + name;
            var mode = entry.Mode;

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    if (File.Exists(hostPath))
                    {
                        throw new IOException(
                            $"Cannot create the directory '{hostPath}' for archive entry '{entry.Name}': a file of that name exists.");
                    }

                    Directory.CreateDirectory(hostPath);
                    directories.Add((hostPath, mode, entry.ModificationTime));
                    directoryCount++;
                    entries.Add(new CopiedEntry(containerEntryPath, hostPath, 0, mode, IsDirectory: true,
                        IsExecutable: false));
                    break;

                case TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile:
                {
                    if (string.Equals(hostPath, root, PathComparison))
                    {
                        throw EscapeException(entry.Name);
                    }

                    if (!options.Overwrite && (File.Exists(hostPath) || Directory.Exists(hostPath)))
                    {
                        skipped.Add(entry.Name);
                        break;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(hostPath) ?? root);
                    var length = await WriteFileAsync(entry, hostPath, cancellationToken).ConfigureAwait(false);
                    ApplyFileMetadata(hostPath, mode, entry.ModificationTime, options);

                    fileCount++;
                    totalBytes += length;
                    entries.Add(new CopiedEntry(containerEntryPath, hostPath, length, mode, IsDirectory: false,
                        IsExecutable: (mode & AnyExecute) != 0));
                    options.Progress?.Report(new CopyProgress(fileCount, totalBytes,
                        Path.GetRelativePath(root, hostPath).Replace('\\', '/')));
                    break;
                }

                case TarEntryType.HardLink:
                {
                    var linkName = NormalizeName(entry.LinkName);
                    if (stripping && linkName.StartsWith(archiveRoot + "/", StringComparison.Ordinal))
                    {
                        linkName = linkName[(archiveRoot.Length + 1)..];
                    }

                    var source = Resolve(root, rootWithSeparator, linkName, entry.LinkName);
                    if (!File.Exists(source)
                        || (!options.Overwrite && (File.Exists(hostPath) || Directory.Exists(hostPath))))
                    {
                        skipped.Add(entry.Name);
                        break;
                    }

                    // Written as a copy: hard links are not portable across host file systems.
                    Directory.CreateDirectory(Path.GetDirectoryName(hostPath) ?? root);
                    File.Copy(source, hostPath, overwrite: true);
                    ApplyFileMetadata(hostPath, mode, entry.ModificationTime, options);

                    var length = new FileInfo(hostPath).Length;
                    fileCount++;
                    totalBytes += length;
                    entries.Add(new CopiedEntry(containerEntryPath, hostPath, length, mode, IsDirectory: false,
                        IsExecutable: (mode & AnyExecute) != 0));
                    options.Progress?.Report(new CopyProgress(fileCount, totalBytes,
                        Path.GetRelativePath(root, hostPath).Replace('\\', '/')));
                    break;
                }

                case TarEntryType.SymbolicLink:
                    if (options.SkipSymlinks || !TryCreateSymlink(root, rootWithSeparator, hostPath, entry.LinkName,
                            options.Overwrite))
                    {
                        skipped.Add(entry.Name);
                        break;
                    }

                    entries.Add(new CopiedEntry(containerEntryPath, hostPath, 0, mode, IsDirectory: false,
                        IsExecutable: false));
                    break;

                default:
                    // Devices, pipes and anything else a host folder cannot sensibly hold.
                    skipped.Add(entry.Name);
                    break;
            }
        }

        // Directory modes and times last, deepest first: a read-only folder must not stop its own
        // contents from being written, and writing a child updates its parent's modification time.
        for (var i = directories.Count - 1; i >= 0; i--)
        {
            var (path, mode, modified) = directories[i];
            if (options.ApplyUnixModes && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, mode);
            }

            Directory.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        }

        return new CopyFromContainerResult
        {
            Entries = entries,
            TotalBytes = totalBytes,
            FileCount = fileCount,
            DirectoryCount = directoryCount,
            SkippedEntries = skipped,
        };
    }

    /// <summary>
    /// Maps an archive-relative name to a full host path, refusing rooted names, drive letters and
    /// anything that resolves outside the destination.
    /// </summary>
    internal static string Resolve(string root, string rootWithSeparator, string relative, string entryName)
    {
        if (relative.StartsWith('/') || relative.StartsWith('\\') || Path.IsPathRooted(relative)
            || (relative.Length >= 2 && relative[1] == ':' && char.IsAsciiLetter(relative[0])))
        {
            throw EscapeException(entryName);
        }

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        full = Path.TrimEndingDirectorySeparator(full);

        if (!string.Equals(full, root, PathComparison)
            && !full.StartsWith(rootWithSeparator, PathComparison))
        {
            throw EscapeException(entryName);
        }

        return full;
    }

    private static DockerException EscapeException(string entryName) =>
        new($"Refusing to extract archive entry '{entryName}': its path resolves outside the destination folder.");

    /// <summary>
    /// Refuses to write through an existing symbolic link (left by an earlier entry or already present
    /// in the destination) whose target lies outside the destination.
    /// </summary>
    private static void EnsureNoLinkedAncestorEscapes(string root, string rootWithSeparator, string hostPath,
        string entryName)
    {
        for (var current = Path.GetDirectoryName(hostPath);
             current is not null && current.StartsWith(rootWithSeparator, PathComparison);
             current = Path.GetDirectoryName(current))
        {
            var info = new DirectoryInfo(current);
            if (!info.Exists || info.LinkTarget is null)
            {
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            var resolved = target is null ? current : Path.TrimEndingDirectorySeparator(target.FullName);
            if (!string.Equals(resolved, root, PathComparison) && !resolved.StartsWith(rootWithSeparator, PathComparison))
            {
                throw EscapeException(entryName);
            }
        }
    }

    private static bool TryCreateSymlink(string root, string rootWithSeparator, string hostPath, string linkTarget,
        bool overwrite)
    {
        if (string.IsNullOrEmpty(linkTarget) || linkTarget.StartsWith('/') || Path.IsPathRooted(linkTarget))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(hostPath) ?? root;
        var resolved = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(parent, linkTarget.Replace('/', Path.DirectorySeparatorChar))));
        if (!string.Equals(resolved, root, PathComparison) && !resolved.StartsWith(rootWithSeparator, PathComparison))
        {
            return false;
        }

        try
        {
            if (File.Exists(hostPath) || new FileInfo(hostPath).LinkTarget is not null)
            {
                if (!overwrite)
                {
                    return false;
                }

                File.Delete(hostPath);
            }

            Directory.CreateDirectory(parent);
            File.CreateSymbolicLink(hostPath, linkTarget);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows without the symbolic-link privilege, or something already in the way.
            return false;
        }
    }

    private static async Task<long> WriteFileAsync(TarEntry entry, string hostPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var output = new FileStream(hostPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous);

            if (entry.DataStream is not null)
            {
                await entry.DataStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            return output.Length;
        }
        catch
        {
            // Never leave a half-written file behind.
            try
            {
                File.Delete(hostPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The original failure is what matters.
            }

            throw;
        }
    }

    private static void ApplyFileMetadata(string hostPath, UnixFileMode mode, DateTimeOffset modified,
        CopyFromContainerOptions options)
    {
        File.SetLastWriteTimeUtc(hostPath, modified.UtcDateTime);
        if (options.ApplyUnixModes && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hostPath, mode);
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var normalized = name;
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimEnd('/');
    }

    private static string FirstSegment(string name)
    {
        var slash = name.IndexOf('/');
        return slash < 0 ? name : name[..slash];
    }

    /// <summary>
    /// The container folder the archive's root entry sits in, with a trailing slash: <c>/</c> for
    /// <c>/app</c>, <c>/srv/</c> for <c>/srv/app/</c>.
    /// </summary>
    private static string ContainerParent(string containerPath)
    {
        var trimmed = (containerPath ?? string.Empty).TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? "/" : trimmed[..(slash + 1)];
    }
}
