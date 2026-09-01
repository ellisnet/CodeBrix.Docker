namespace CodeBrix.Docker;

/// <summary>
/// One rule violation reported by Hadolint for a Dockerfile.
/// </summary>
/// <param name="Code">The rule identifier, for example <c>DL3008</c> or <c>SC2086</c>.</param>
/// <param name="Level">The severity Hadolint assigned: <c>style</c>, <c>info</c>, <c>warning</c> or <c>error</c>.</param>
/// <param name="Line">The one-based line number in the Dockerfile.</param>
/// <param name="Message">The explanation of what to change.</param>
public sealed record HadolintFinding(string Code, string Level, int Line, string Message)
{
    /// <summary>Gets the one-based column number, when Hadolint reported one.</summary>
    public int Column { get; init; }
}
