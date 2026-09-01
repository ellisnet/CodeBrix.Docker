namespace RedisSetupTool.TerminalView;

/// <summary>How a console session behaves.</summary>
public sealed class ExecTerminalSessionOptions
{
    /// <summary>Gets or sets the colours.</summary>
    public TerminalPalette Palette { get; set; } = TerminalPalette.Dark;

    /// <summary>Gets or sets how many lines of scrollback the control keeps.</summary>
    /// <remarks>This must be applied to the control before it loads.</remarks>
    public int Scrollback { get; set; } = 5000;

    /// <summary>Gets or sets the terminal font size.</summary>
    public float FontSize { get; set; } = 13f;

    /// <summary>Gets or sets how long resize events are coalesced for, in milliseconds.</summary>
    /// <remarks>A window drag fires many resizes and each one is an HTTP round trip.</remarks>
    public int ResizeDebounceMs { get; set; } = 120;

    /// <summary>Gets or sets a value indicating whether an exit banner is written when the shell ends.</summary>
    public bool ExitBanner { get; set; } = true;

    /// <summary>Gets or sets the size of the read buffer, in bytes.</summary>
    public int ReadBufferSize { get; set; } = 8192;
}
