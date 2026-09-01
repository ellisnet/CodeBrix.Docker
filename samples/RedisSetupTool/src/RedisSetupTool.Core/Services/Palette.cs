using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RedisSetupTool.Services;

/// <summary>
/// The app palette as brushes, for the places a view model has to choose a colour itself —
/// selection highlighting, state dots and severity chips. The values are the same ones
/// <c>MainPage.xaml</c> declares as static resources; XAML uses the resources, code uses these.
/// </summary>
public static class Palette
{
    private static Brush Make(byte red, byte green, byte blue) =>
        new SolidColorBrush(Color.FromArgb(0xFF, red, green, blue));

    /// <summary>The page background, <c>#16181D</c>.</summary>
    public static Brush Window { get; } = Make(0x16, 0x18, 0x1D);

    /// <summary>The header and rail background, <c>#1B1E25</c>.</summary>
    public static Brush Header { get; } = Make(0x1B, 0x1E, 0x25);

    /// <summary>The card surface, <c>#1F232B</c>.</summary>
    public static Brush Card { get; } = Make(0x1F, 0x23, 0x2B);

    /// <summary>The recessed well inside a card, <c>#171A20</c>.</summary>
    public static Brush CardWell { get; } = Make(0x17, 0x1A, 0x20);

    /// <summary>The one-pixel divider, <c>#2A2F39</c>.</summary>
    public static Brush Hairline { get; } = Make(0x2A, 0x2F, 0x39);

    /// <summary>The raised surface a selected rail item sits on, <c>#262B34</c>.</summary>
    public static Brush Raised { get; } = Make(0x26, 0x2B, 0x34);

    /// <summary>Primary text, <c>#F2F4F8</c>.</summary>
    public static Brush TextPrimary { get; } = Make(0xF2, 0xF4, 0xF8);

    /// <summary>Secondary text, <c>#A8B0BF</c>.</summary>
    public static Brush TextSecondary { get; } = Make(0xA8, 0xB0, 0xBF);

    /// <summary>Tertiary text, <c>#6E7686</c>.</summary>
    public static Brush TextTertiary { get; } = Make(0x6E, 0x76, 0x86);

    /// <summary>The Redis-red accent, <c>#E05252</c>.</summary>
    public static Brush Accent { get; } = Make(0xE0, 0x52, 0x52);

    /// <summary>The dimmed accent used behind chips, <c>#B33F3F</c>.</summary>
    public static Brush AccentDim { get; } = Make(0xB3, 0x3F, 0x3F);

    /// <summary>Healthy / running green, <c>#4FBF7B</c>.</summary>
    public static Brush Good { get; } = Make(0x4F, 0xBF, 0x7B);

    /// <summary>Partly-there amber, <c>#D9A05B</c>.</summary>
    public static Brush Warn { get; } = Make(0xD9, 0xA0, 0x5B);

    /// <summary>Stopped / unknown grey, <c>#6E7686</c>.</summary>
    public static Brush Idle { get; } = Make(0x6E, 0x76, 0x86);

    /// <summary>Failed red, the same value as the accent.</summary>
    public static Brush Bad { get; } = Make(0xE0, 0x52, 0x52);

    /// <summary>Nothing painted at all — a rail item that is not selected.</summary>
    public static Brush Transparent { get; } = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
}
