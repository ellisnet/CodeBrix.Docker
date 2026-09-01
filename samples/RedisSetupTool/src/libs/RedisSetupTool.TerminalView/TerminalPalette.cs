using SkiaSharp;

namespace RedisSetupTool.TerminalView;

/// <summary>The three colours a terminal control needs.</summary>
public sealed class TerminalPalette
{
    /// <summary>Creates a palette.</summary>
    /// <param name="background">The background colour.</param>
    /// <param name="foreground">The default text colour.</param>
    /// <param name="selection">The selection colour, normally translucent.</param>
    public TerminalPalette(SKColor background, SKColor foreground, SKColor selection)
    {
        Background = background;
        Foreground = foreground;
        Selection = selection;
    }

    /// <summary>Gets the palette matching the application's dark theme.</summary>
    /// <remarks>The background is the card-well colour, so a console sits inside a card cleanly.</remarks>
    public static TerminalPalette Dark { get; } = new(
        new SKColor(0x17, 0x1A, 0x20),
        new SKColor(0xF2, 0xF4, 0xF8),
        new SKColor(0xE0, 0x52, 0x52, 0x66));

    /// <summary>Gets the plain black-on-white palette.</summary>
    public static TerminalPalette Classic { get; } = new(
        new SKColor(0x00, 0x00, 0x00),
        new SKColor(0xFF, 0xFF, 0xFF),
        new SKColor(0x4D, 0x8B, 0xD8, 0x66));

    /// <summary>Gets the background colour.</summary>
    public SKColor Background { get; }

    /// <summary>Gets the default text colour.</summary>
    public SKColor Foreground { get; }

    /// <summary>Gets the selection colour.</summary>
    public SKColor Selection { get; }
}
