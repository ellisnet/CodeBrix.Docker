namespace CodeBrix.Docker;

/// <summary>
/// The outcome of one read from a <see cref="ContainerExecStream"/>.
/// </summary>
/// <param name="Target">Which stream the bytes came from.</param>
/// <param name="Count">How many bytes were written into the caller's buffer.</param>
public readonly record struct ExecStreamReadResult(ExecStreamTarget Target, int Count)
{
    /// <summary>Gets a value indicating whether the exec stream has ended.</summary>
    public bool EndOfStream => Count == 0;
}
