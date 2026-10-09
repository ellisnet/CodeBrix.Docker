using System;
using System.IO;

namespace CodeBrix.Docker;

/// <summary>
/// What the daemon reports about one path inside a container, from
/// <see cref="ContainerOperations.StatPathAsync"/>.
/// </summary>
/// <param name="Name">The base name of the path, for example <c>hosts</c> for <c>/etc/hosts</c>.</param>
/// <param name="Size">The size in bytes. For a directory this is the file system's own figure, not a total.</param>
/// <param name="Mode">
/// The raw mode word exactly as the daemon sent it: a Go <c>os.FileMode</c>, whose type flags live in
/// the high bits (directory is bit 31, symbolic link bit 27) and whose permission bits are the low nine.
/// Use <see cref="IsDirectory"/>, <see cref="IsSymlink"/>, <see cref="IsRegularFile"/> and
/// <see cref="PermissionBits"/> rather than decoding it by hand.
/// </param>
/// <param name="Modified">The last modification time.</param>
/// <param name="LinkTarget">
/// The target of a symbolic link, as written in the link, or an empty string for anything else. The
/// daemon does not follow the link: the other members describe the link itself.
/// </param>
public sealed record ContainerPathStat(string Name, long Size, uint Mode, DateTimeOffset Modified, string LinkTarget)
{
    /// <summary>Go's <c>os.ModeDir</c>.</summary>
    internal const uint GoModeDir = 1u << 31;

    /// <summary>Go's <c>os.ModeSymlink</c>.</summary>
    internal const uint GoModeSymlink = 1u << 27;

    /// <summary>Go's <c>os.ModeSetuid</c>.</summary>
    internal const uint GoModeSetuid = 1u << 23;

    /// <summary>Go's <c>os.ModeSetgid</c>.</summary>
    internal const uint GoModeSetgid = 1u << 22;

    /// <summary>Go's <c>os.ModeSticky</c>.</summary>
    internal const uint GoModeSticky = 1u << 20;

    /// <summary>
    /// Go's <c>os.ModeType</c>: directory, symbolic link, named pipe, socket, device, character device
    /// and irregular file.
    /// </summary>
    internal const uint GoModeType = GoModeDir | GoModeSymlink | (1u << 25) | (1u << 24) | (1u << 26)
                                     | (1u << 21) | (1u << 19);

    /// <summary>Gets a value indicating whether the path is a directory.</summary>
    public bool IsDirectory => (Mode & GoModeDir) != 0;

    /// <summary>Gets a value indicating whether the path is a symbolic link.</summary>
    public bool IsSymlink => (Mode & GoModeSymlink) != 0;

    /// <summary>
    /// Gets a value indicating whether the path is a regular file: none of the type bits (directory,
    /// symbolic link, pipe, socket, device, irregular) is set.
    /// </summary>
    public bool IsRegularFile => (Mode & GoModeType) == 0;

    /// <summary>
    /// Gets the permission bits — read, write and execute for user, group and other, plus set-user-id,
    /// set-group-id and sticky — translated from Go's layout into <see cref="UnixFileMode"/>.
    /// </summary>
    public UnixFileMode PermissionBits
    {
        get
        {
            var bits = (UnixFileMode)(Mode & 0x1FF);
            if ((Mode & GoModeSetuid) != 0)
            {
                bits |= UnixFileMode.SetUser;
            }

            if ((Mode & GoModeSetgid) != 0)
            {
                bits |= UnixFileMode.SetGroup;
            }

            if ((Mode & GoModeSticky) != 0)
            {
                bits |= UnixFileMode.StickyBit;
            }

            return bits;
        }
    }
}
