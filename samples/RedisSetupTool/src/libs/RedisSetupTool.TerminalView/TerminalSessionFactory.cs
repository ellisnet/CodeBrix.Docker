using System;
using CodeBrix.Platform.UI.TerminalView;
using RedisSetupTool.DockerManagement.Exec;

namespace RedisSetupTool.TerminalView;

/// <summary>
/// Builds a configured control, its sink and the pump that joins them. The control is created in code
/// rather than in XAML because it declares no dependency properties and so cannot be bound, styled or
/// placed inside a data template.
/// </summary>
public static class TerminalSessionFactory
{
    /// <summary>Creates a control with the palette, scrollback and font size applied.</summary>
    /// <param name="options">The session options; null selects the defaults.</param>
    /// <returns>The control, ready to be added to the visual tree.</returns>
    /// <remarks>Scrollback must be set before the control loads, which is why this exists.</remarks>
    public static TerminalControl CreateControl(ExecTerminalSessionOptions options = null)
    {
        var settings = options ?? new ExecTerminalSessionOptions();
        var palette = settings.Palette ?? TerminalPalette.Dark;

        return new TerminalControl
        {
            Scrollback = settings.Scrollback,
            TerminalFontSize = settings.FontSize,
            BackgroundColor = palette.Background,
            ForegroundColor = palette.Foreground,
            SelectionColor = palette.Selection,
        };
    }

    /// <summary>Joins an exec session to a control.</summary>
    /// <param name="session">The exec session.</param>
    /// <param name="control">The control that shows it.</param>
    /// <param name="options">The session options; null selects the defaults.</param>
    /// <returns>The pump, not yet started.</returns>
    /// <remarks>
    /// Call this from the control's Loaded handler: output fed before the control has loaded is
    /// silently dropped. Start the returned pump only after the handlers below are wired.
    /// </remarks>
    public static ExecTerminalSession Attach(IExecSession session, TerminalControl control,
        ExecTerminalSessionOptions options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(control);

        var pump = new ExecTerminalSession(session, new TerminalControlSink(control), options);
        control.InputEmitted += pump.OnInput;
        control.GridResized += pump.OnGridResized;
        return pump;
    }

    /// <summary>Unhooks the handlers <see cref="Attach"/> added.</summary>
    /// <param name="pump">The pump.</param>
    /// <param name="control">The control it was attached to.</param>
    public static void Detach(ExecTerminalSession pump, TerminalControl control)
    {
        if (pump is null || control is null)
        {
            return;
        }

        control.InputEmitted -= pump.OnInput;
        control.GridResized -= pump.OnGridResized;
    }
}
