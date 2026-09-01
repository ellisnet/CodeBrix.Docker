namespace RedisSetupTool.TerminalView;

/// <summary>
/// Where the pump writes. Bytes go through untouched: decoding a partial read is exactly the bug this
/// seam avoids. Having it as an interface is also what lets the pump be tested with no control, no
/// Skia natives and no window.
/// </summary>
public interface ITerminalSink
{
    /// <summary>Writes bytes straight to the terminal.</summary>
    /// <param name="data">The buffer.</param>
    /// <param name="length">How many bytes of it are real.</param>
    void Feed(byte[] data, int length);

    /// <summary>Writes text the session generated itself, such as an exit banner.</summary>
    /// <param name="text">The text.</param>
    void Feed(string text);

    /// <summary>Resets the terminal.</summary>
    void Reset();
}
