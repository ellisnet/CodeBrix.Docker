namespace CodeBrix.Docker;

/// <summary>
/// One progress report from a directory or file copy into or out of a container.
/// </summary>
/// <param name="FilesCopied">Regular files copied so far, including the one just finished.</param>
/// <param name="BytesCopied">File content bytes copied so far.</param>
/// <param name="CurrentPath">
/// The path just copied, relative and with forward slashes: the archive path for a copy into a
/// container, the host path for a copy out of one.
/// </param>
public sealed record CopyProgress(long FilesCopied, long BytesCopied, string CurrentPath);
