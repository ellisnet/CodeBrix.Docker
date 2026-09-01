namespace RedisSetupTool.DockerManagement.Exec;

/// <summary>What one read off an exec session produced.</summary>
/// <param name="Kind">Which stream the bytes came from.</param>
/// <param name="Count">How many bytes were written into the buffer.</param>
public readonly record struct ExecReadResult(ExecStreamKind Kind, int Count)
{
    /// <summary>Gets a value indicating whether the session has closed.</summary>
    public bool EndOfStream => Count == 0;
}
