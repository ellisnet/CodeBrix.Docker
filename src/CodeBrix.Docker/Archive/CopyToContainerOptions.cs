using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Docker;

/// <summary>
/// Options for copying into a container. <see cref="NoOverwriteDirNonDir"/> and
/// <see cref="CopyUidGid"/> apply to every copy, including a caller-built archive passed to
/// <see cref="ContainerOperations.CopyToContainerAsync"/>; everything else shapes the archive that
/// <see cref="ContainerOperations.CopyDirectoryToContainerAsync"/> and
/// <see cref="ContainerOperations.CopyFileToContainerAsync"/> build from the host file system.
/// </summary>
public sealed class CopyToContainerOptions
{
    /// <summary>The default mode for files: <c>0644</c>.</summary>
    internal const UnixFileMode DefaultFileModeValue =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>The default mode for directories and executable files: <c>0755</c>.</summary>
    internal const UnixFileMode DefaultDirectoryModeValue =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>
    /// Gets or sets glob patterns for paths to leave out, matched against each path relative to the
    /// host directory with forward slashes. A pattern without a slash matches a file or folder of
    /// that name at any depth (<c>target</c>, <c>*.log</c>); a pattern containing a slash is anchored
    /// at the host directory (<c>build/output</c>, <c>docs/**/*.tmp</c>). A trailing slash limits a
    /// pattern to directories. <c>*</c> and <c>?</c> stay within one segment, <c>**</c> crosses
    /// segments. An excluded directory is skipped with everything beneath it. Matching is case
    /// sensitive. Empty by default.
    /// </summary>
    public IList<string> ExcludePatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the host folder itself becomes the archive's root, so
    /// that copying <c>src</c> to <c>/work</c> produces <c>/work/src/...</c> rather than placing its
    /// contents directly in <c>/work</c>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool IncludeRootFolder { get; set; }

    /// <summary>
    /// Gets or sets the mode given to files when host modes are not preserved. Defaults to <c>0644</c>.
    /// </summary>
    public UnixFileMode DefaultFileMode { get; set; } = DefaultFileModeValue;

    /// <summary>
    /// Gets or sets the mode given to directories when host modes are not preserved. Defaults to
    /// <c>0755</c>.
    /// </summary>
    public UnixFileMode DefaultDirectoryMode { get; set; } = DefaultDirectoryModeValue;

    /// <summary>
    /// Gets or sets the mode given to files recognised as executable when host modes are not
    /// preserved: on a Unix host a file with any execute bit set, on any host a file whose extension
    /// is in <see cref="ExecutableExtensions"/>. <see langword="null"/> gives executables
    /// <see cref="DefaultFileMode"/> like everything else. Defaults to <c>0755</c>.
    /// </summary>
    public UnixFileMode? ExecutableFileMode { get; set; } = DefaultDirectoryModeValue;

    /// <summary>
    /// Gets or sets file extensions (<c>.sh</c> or <c>sh</c>, case insensitive) that mark a file as
    /// executable for <see cref="ExecutableFileMode"/>. This is how a Windows host, which has no
    /// execute bit, gets a runnable script into a container. Empty by default.
    /// </summary>
    public IList<string> ExecutableExtensions { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether each file's and directory's own permission bits are read
    /// from the host and used verbatim. Defaults to <see langword="true"/> on every host except
    /// Windows, where it is ignored even if set because there are no modes to read.
    /// </summary>
    public bool PreserveUnixModes { get; set; } = !OperatingSystem.IsWindows();

    /// <summary>
    /// Gets or sets a value indicating whether the daemon refuses to replace an existing directory
    /// with a non-directory or the other way round (the Engine API's <c>noOverwriteDirNonDir</c>).
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool NoOverwriteDirNonDir { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the archive's user and group ids are applied inside the
    /// container (the Engine API's <c>copyUIDGID</c>). The archives this library builds always carry
    /// uid and gid 0. Defaults to <see langword="false"/>, which leaves ownership to the daemon.
    /// </summary>
    public bool CopyUidGid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether symbolic links on the host are followed and their
    /// targets copied as ordinary files and folders. Defaults to <see langword="false"/>: a link is
    /// recorded in the archive as a link, with its target text unchanged. A link that cannot be
    /// resolved is recorded as a link either way, and a folder already visited is not entered twice.
    /// </summary>
    public bool FollowSymlinks { get; set; }

    /// <summary>Gets or sets an optional sink that receives a report after each file is added.</summary>
    public IProgress<CopyProgress> Progress { get; set; }
}
