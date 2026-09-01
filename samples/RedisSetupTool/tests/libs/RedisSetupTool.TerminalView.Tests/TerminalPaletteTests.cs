using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace RedisSetupTool.TerminalView.Tests;

/// <summary>Covers the two palette presets.</summary>
public class TerminalPaletteTests
{
    /// <summary>The dark preset's background is the application's card-well colour.</summary>
    [Fact]
    public void Dark_MatchesTheApplicationPalette()
    {
        //Arrange
        var palette = TerminalPalette.Dark;

        //Assert
        palette.Background.Should().Be(new SKColor(0x17, 0x1A, 0x20));
        palette.Foreground.Should().Be(new SKColor(0xF2, 0xF4, 0xF8));
        palette.Selection.Alpha.Should().Be((byte)0x66);
    }

    /// <summary>The classic preset is black on white.</summary>
    [Fact]
    public void Classic_IsBlackAndWhite()
    {
        //Arrange
        var palette = TerminalPalette.Classic;

        //Assert
        palette.Background.Should().Be(new SKColor(0, 0, 0));
        palette.Foreground.Should().Be(new SKColor(0xFF, 0xFF, 0xFF));
    }
}
