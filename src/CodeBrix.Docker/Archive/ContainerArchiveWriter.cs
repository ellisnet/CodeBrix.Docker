using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Docker;

/// <summary>
/// Writes a host folder or file to a tar stream as the archive the Engine API's
/// <c>PUT /containers/{id}/archive</c> expects: forward-slash names, modes chosen by
/// <see cref="CopyToContainerOptions"/>, uid and gid 0, and file content streamed straight from disk.
/// </summary>
internal sealed class ContainerArchiveWriter
{
    private const UnixFileMode AnyExecute =
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private readonly CopyToContainerOptions _options;
    private readonly ExcludeMatcher _excludes;
    private readonly HashSet<string> _executableExtensions;
    private readonly HashSet<string> _visitedDirectories;
    private long _files;
    private long _bytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerArchiveWriter"/> class.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public ContainerArchiveWriter(CopyToContainerOptions options)
    {
        _options = options ?? new CopyToContainerOptions();
        _excludes = new ExcludeMatcher(_options.ExcludePatterns);
        _executableExtensions = new HashSet<string>(
            (_options.ExecutableExtensions ?? []).Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.Trim().TrimStart('.')),
            StringComparer.OrdinalIgnoreCase);
        _visitedDirectories = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes a folder's contents, or the folder itself when
    /// <see cref="CopyToContainerOptions.IncludeRootFolder"/> is set, as a complete tar archive.
    /// </summary>
    /// <param name="destination">The stream to write to. It is left open.</param>
    /// <param name="hostDirectory">The folder to archive.</param>
    /// <param name="cancellationToken">A cancellation token, checked between and during entries.</param>
    /// <returns>A task that completes when the archive's end marker has been written.</returns>
    public async Task WriteDirectoryAsync(Stream destination, string hostDirectory,
        CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(Path.GetFullPath(hostDirectory));
        await using var writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);

        var prefix = string.Empty;
        if (_options.IncludeRootFolder)
        {
            prefix = root.Name.Length == 0 ? string.Empty : root.Name + "/";
            if (prefix.Length > 0)
            {
                await WriteDirectoryEntryAsync(writer, root, prefix, cancellationToken).ConfigureAwait(false);
            }
        }

        _visitedDirectories.Add(root.FullName);
        await WriteChildrenAsync(writer, root, prefix, relativePrefix: string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one file as a complete, single-entry tar archive named after the file.
    /// </summary>
    /// <param name="destination">The stream to write to. It is left open.</param>
    /// <param name="hostFilePath">The file to archive.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the archive's end marker has been written.</returns>
    public async Task WriteFileAsync(Stream destination, string hostFilePath, CancellationToken cancellationToken)
    {
        var file = new FileInfo(Path.GetFullPath(hostFilePath));
        await using var writer = new TarWriter(destination, TarEntryFormat.Pax, leaveOpen: true);
        await WriteFileEntryAsync(writer, file, file.Name, file.FullName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Chooses a file's archive mode from its host mode (when there is one) and its name.
    /// </summary>
    /// <param name="hostMode">The host's permission bits, or <see langword="null"/> on a Windows host.</param>
    /// <param name="fileName">The file name, for the extension test.</param>
    /// <returns>The mode to record.</returns>
    internal UnixFileMode ResolveFileMode(UnixFileMode? hostMode, string fileName)
    {
        if (hostMode.HasValue && _options.PreserveUnixModes)
        {
            return hostMode.Value;
        }

        var executable = (hostMode.HasValue && (hostMode.Value & AnyExecute) != 0) || HasExecutableExtension(fileName);
        return executable && _options.ExecutableFileMode.HasValue
            ? _options.ExecutableFileMode.Value
            : _options.DefaultFileMode;
    }

    /// <summary>
    /// Chooses a directory's archive mode from its host mode, when there is one.
    /// </summary>
    /// <param name="hostMode">The host's permission bits, or <see langword="null"/> on a Windows host.</param>
    /// <returns>The mode to record.</returns>
    internal UnixFileMode ResolveDirectoryMode(UnixFileMode? hostMode) =>
        hostMode.HasValue && _options.PreserveUnixModes ? hostMode.Value : _options.DefaultDirectoryMode;

    private async Task WriteChildrenAsync(TarWriter writer, DirectoryInfo directory, string prefix,
        string relativePrefix, CancellationToken cancellationToken)
    {
        var children = directory.EnumerateFileSystemInfos()
            .OrderBy(child => child.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = relativePrefix + child.Name;
            var isLink = child.LinkTarget is not null;
            var isDirectory = child is DirectoryInfo;

            if (_excludes.IsExcluded(relative, isDirectory))
            {
                continue;
            }

            if (isLink)
            {
                var target = _options.FollowSymlinks ? ResolveLink(child) : null;
                if (target is null)
                {
                    await WriteSymlinkEntryAsync(writer, child, prefix + relative, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (target is DirectoryInfo targetDirectory)
                {
                    if (!_visitedDirectories.Add(targetDirectory.FullName))
                    {
                        continue;
                    }

                    await WriteDirectoryEntryAsync(writer, targetDirectory, prefix + relative + "/",
                        cancellationToken).ConfigureAwait(false);
                    await WriteChildrenAsync(writer, targetDirectory, prefix, relative + "/", cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await WriteFileEntryAsync(writer, (FileInfo)target, prefix + relative, prefix + relative,
                        cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (child is DirectoryInfo childDirectory)
            {
                _visitedDirectories.Add(childDirectory.FullName);
                await WriteDirectoryEntryAsync(writer, childDirectory, prefix + relative + "/", cancellationToken)
                    .ConfigureAwait(false);
                await WriteChildrenAsync(writer, childDirectory, prefix, relative + "/", cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await WriteFileEntryAsync(writer, (FileInfo)child, prefix + relative, prefix + relative,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteDirectoryEntryAsync(TarWriter writer, DirectoryInfo directory, string entryName,
        CancellationToken cancellationToken)
    {
        var entry = new PaxTarEntry(TarEntryType.Directory, entryName)
        {
            Mode = ResolveDirectoryMode(ReadHostMode(directory)),
            ModificationTime = directory.LastWriteTimeUtc,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSymlinkEntryAsync(TarWriter writer, FileSystemInfo link, string entryName,
        CancellationToken cancellationToken)
    {
        var target = link.LinkTarget ?? string.Empty;
        if (OperatingSystem.IsWindows())
        {
            target = target.Replace('\\', '/');
        }

        var entry = new PaxTarEntry(TarEntryType.SymbolicLink, entryName)
        {
            LinkName = target,
            Mode = CopyToContainerOptions.DefaultDirectoryModeValue | UnixFileMode.UserWrite
                   | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite,
            ModificationTime = link.LastWriteTimeUtc,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        Report(entryName);
    }

    private async Task WriteFileEntryAsync(TarWriter writer, FileInfo file, string entryName, string reportName,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() && file.Length == 0 && !IsRegularFile(file.FullName))
        {
            // A pipe, socket or device node: opening a pipe for reading would block until a writer
            // appeared, and none of them has content a container could use.
            return;
        }

        await using var content = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            Mode = ResolveFileMode(ReadHostMode(file), file.Name),
            ModificationTime = file.LastWriteTimeUtc,
            DataStream = content,
        };

        await writer.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);

        _files++;
        _bytes += content.Length;
        Report(reportName);
    }

    private void Report(string currentPath) =>
        _options.Progress?.Report(new CopyProgress(_files, _bytes, currentPath));

    private bool HasExecutableExtension(string fileName)
    {
        if (_executableExtensions.Count == 0)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 1 && _executableExtensions.Contains(extension[1..]);
    }

    private static FileSystemInfo ResolveLink(FileSystemInfo link)
    {
        try
        {
            var target = link.ResolveLinkTarget(returnFinalTarget: true);
            return target is { Exists: true } ? target : null;
        }
        catch (IOException)
        {
            // A link loop or an unreadable target: record the link itself.
            return null;
        }
    }

    private static UnixFileMode? ReadHostMode(FileSystemInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        return info.UnixFileMode;
    }

    /// <summary>
    /// Asks the in-box tar writer, which reads the node type without opening the file, whether an
    /// empty-looking path is a regular file. Used only for zero-length files.
    /// </summary>
    private static bool IsRegularFile(string path)
    {
        try
        {
            using var probe = new MemoryStream();
            using (var writer = new TarWriter(probe, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(path, "probe");
            }

            probe.Position = 0;
            using var reader = new TarReader(probe);
            var entry = reader.GetNextEntry();
            return entry?.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
