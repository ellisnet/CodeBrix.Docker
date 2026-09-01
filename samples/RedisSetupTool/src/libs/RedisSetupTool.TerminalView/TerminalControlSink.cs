using System;
using CodeBrix.Platform.UI.TerminalView;

namespace RedisSetupTool.TerminalView;

/// <summary>
/// The only type in the application that names <c>TerminalControl</c>. Everything else drives the
/// console through <see cref="ITerminalSink"/>.
/// </summary>
public sealed class TerminalControlSink : ITerminalSink
{
    private readonly TerminalControl _control;

    /// <summary>Creates the adapter.</summary>
    /// <param name="control">The control to feed.</param>
    public TerminalControlSink(TerminalControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    /// <inheritdoc />
    /// <remarks>The control's Feed is thread safe: it copies the buffer and enqueues on the
    /// dispatcher, so the pump can call it from a worker thread.</remarks>
    public void Feed(byte[] data, int length) => _control.Feed(data, length);

    /// <inheritdoc />
    public void Feed(string text) => _control.Feed(text);

    /// <inheritdoc />
    public void Reset() => _control.Reset();
}
