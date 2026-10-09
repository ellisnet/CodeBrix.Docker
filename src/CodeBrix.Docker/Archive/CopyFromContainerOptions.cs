using System;

namespace CodeBrix.Docker;

/// <summary>
/// Options for <see cref="ContainerOperations.CopyFromContainerToDirectoryAsync"/>.
/// </summary>
public sealed class CopyFromContainerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the archive's root folder is dropped. The daemon names
    /// the archive's root entry after the last segment of the container path, so copying <c>/app</c>
    /// to <c>out</c> produces <c>out/app/...</c>; with this set and <c>/app</c> a directory, its
    /// contents land directly in <c>out</c>. Has no effect when the container path is a file.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool StripRootFolder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether existing host files are replaced. When
    /// <see langword="false"/>, an existing file is left alone and its entry is listed in
    /// <see cref="CopyFromContainerResult.SkippedEntries"/>. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the permission bits the archive carries are applied to
    /// what is written. Defaults to <see langword="true"/> on every host except Windows, where it is
    /// ignored even if set: the modes are still recorded in <see cref="CopiedEntry.Mode"/>.
    /// </summary>
    public bool ApplyUnixModes { get; set; } = !OperatingSystem.IsWindows();

    /// <summary>
    /// Gets or sets a value indicating whether symbolic links are skipped. Defaults to
    /// <see langword="true"/>. When <see langword="false"/>, a link is recreated only when its target
    /// is relative and resolves inside the destination directory; any other link is skipped. Either
    /// way, skipped links are listed in <see cref="CopyFromContainerResult.SkippedEntries"/>.
    /// </summary>
    public bool SkipSymlinks { get; set; } = true;

    /// <summary>Gets or sets an optional sink that receives a report after each file is written.</summary>
    public IProgress<CopyProgress> Progress { get; set; }
}
