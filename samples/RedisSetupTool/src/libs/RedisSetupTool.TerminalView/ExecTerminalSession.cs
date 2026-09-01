using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using RedisSetupTool.DockerManagement.Exec;

namespace RedisSetupTool.TerminalView;

/// <summary>
/// The pump between an exec session and a terminal. It reads bytes and feeds them on, forwards
/// keystrokes, and turns grid resizes into terminal resizes - swapping the argument order on the way,
/// because the control reports (columns, rows) and the daemon takes (rows, columns).
/// </summary>
public sealed class ExecTerminalSession : IAsyncDisposable
{
    private readonly IExecSession _session;
    private readonly ITerminalSink _sink;
    private readonly ExecTerminalSessionOptions _options;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private Task _pump;
    private Timer _resizeTimer;
    private int _pendingColumns;
    private int _pendingRows;
    private int _disposed;

    /// <summary>Creates the pump.</summary>
    /// <param name="session">The exec session to drive.</param>
    /// <param name="sink">Where output goes.</param>
    /// <param name="options">How the session behaves; null selects the defaults.</param>
    public ExecTerminalSession(IExecSession session, ITerminalSink sink,
        ExecTerminalSessionOptions options = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _options = options ?? new ExecTerminalSessionOptions();
        State = TerminalSessionState.Starting;
    }

    /// <summary>Raised when the session's state changes; may arrive on a worker thread.</summary>
    public event Action<TerminalSessionState> StateChanged;

    /// <summary>Raised when the grid size changes, for the status strip.</summary>
    public event Action<int, int> GridChanged;

    /// <summary>Gets the container the shell runs in.</summary>
    public string ContainerId => _session.ContainerId;

    /// <summary>Gets the shell that was resolved.</summary>
    public string ShellPath => _session.ShellPath;

    /// <summary>Gets where the session is in its life.</summary>
    public TerminalSessionState State { get; private set; }

    /// <summary>Gets the last known column count.</summary>
    public int Columns { get; private set; }

    /// <summary>Gets the last known row count.</summary>
    public int Rows { get; private set; }

    /// <summary>Gets the shell's exit code, once it has exited.</summary>
    public long ExitCode { get; private set; }

    /// <summary>Starts the read pump. Calling it twice does nothing the second time.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_pump is not null)
            {
                return;
            }

            SetState(TerminalSessionState.Running);
            _pump = Task.Run(PumpAsync);
        }
    }

    /// <summary>Forwards a keystroke or a paste to the shell.</summary>
    /// <param name="data">The VT-encoded input.</param>
    public void OnInput(string data)
    {
        if (string.IsNullOrEmpty(data) || _cancellation.IsCancellationRequested)
        {
            return;
        }

        _ = SendAsync(data);
    }

    /// <summary>Records a new grid size and resizes the terminal after the debounce window.</summary>
    /// <param name="columns">The new column count.</param>
    /// <param name="rows">The new row count.</param>
    public void OnGridResized(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0 || _cancellation.IsCancellationRequested)
        {
            return;
        }

        Columns = columns;
        Rows = rows;
        GridChanged?.Invoke(columns, rows);

        lock (_gate)
        {
            _pendingColumns = columns;
            _pendingRows = rows;

            var due = Math.Max(0, _options.ResizeDebounceMs);
            if (_resizeTimer is null)
            {
                _resizeTimer = new Timer(_ => FlushResize(), null, due, Timeout.Infinite);
            }
            else
            {
                _resizeTimer.Change(due, Timeout.Infinite);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _cancellation.CancelAsync().ConfigureAwait(false);

        Timer timer;
        Task pump;
        lock (_gate)
        {
            timer = _resizeTimer;
            _resizeTimer = null;
            pump = _pump;
        }

        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        if (pump is not null)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (Exception)
            {
                //The pump swallows its own failures; this guards against a torn-down task.
            }
        }

        await _session.DisposeAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }

    private async Task SendAsync(string data)
    {
        try
        {
            await _session.SendAsync(data, _cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            //The session is going away.
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void FlushResize()
    {
        int columns;
        int rows;
        lock (_gate)
        {
            columns = _pendingColumns;
            rows = _pendingRows;
        }

        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        _ = ResizeAsync(rows, columns);
    }

    private async Task ResizeAsync(int rows, int columns)
    {
        try
        {
            //The daemon takes rows first; the control reports columns first.
            await _session.ResizeAsync(rows, columns, _cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            //The session is going away.
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[Math.Max(256, _options.ReadBufferSize)];

        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var read = await _session.ReadAsync(buffer, _cancellation.Token)
                    .ConfigureAwait(false);
                if (read.EndOfStream)
                {
                    break;
                }

                _sink.Feed(buffer, read.Count);
            }

            ExitCode = await _session.WaitForExitAsync(_cancellation.Token).ConfigureAwait(false);

            if (_options.ExitBanner)
            {
                _sink.Feed("\r\n\x1b[2m[process exited with code "
                    + ExitCode.ToString(CultureInfo.InvariantCulture) + "]\x1b[0m\r\n");
            }

            SetState(TerminalSessionState.Exited);
        }
        catch (OperationCanceledException)
        {
            //Disposal cancels the pump; that is not a failure.
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        if (State == TerminalSessionState.Failed)
        {
            return;
        }

        if (_options.ExitBanner)
        {
            _sink.Feed("\r\n\x1b[31m[session failed: " + exception.Message + "]\x1b[0m\r\n");
        }

        SetState(TerminalSessionState.Failed);
    }

    private void SetState(TerminalSessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
