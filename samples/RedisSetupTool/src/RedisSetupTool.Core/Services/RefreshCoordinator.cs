using Microsoft.UI.Xaml;
using System;

namespace RedisSetupTool.Services;

/// <summary>
/// One <see cref="DispatcherTimer"/> driving the app's periodic refresh. It exists so that
/// exactly one timer ticks for the whole application instead of one per section, and so the
/// tick can be paused while a long operation (creating an instance, tearing one down) is in
/// flight. Construct it on the UI thread.
/// </summary>
public sealed class RefreshCoordinator
{
    private readonly DispatcherTimer _timer = new();
    private bool _isPaused;

    /// <summary>Creates the coordinator with the default six-second period.</summary>
    public RefreshCoordinator()
    {
        _timer.Interval = TimeSpan.FromSeconds(6);
        _timer.Tick += (_, _) =>
        {
            if (_isPaused) { return; }
            Tick?.Invoke();
        };
    }

    /// <summary>Raised on the UI thread on every unpaused tick.</summary>
    public event Action Tick;

    /// <summary>How long the coordinator waits between ticks.</summary>
    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    /// <summary>Whether the timer is running (ticks may still be suppressed by <see cref="Pause"/>).</summary>
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>Starts ticking.</summary>
    public void Start() => _timer.Start();

    /// <summary>Stops ticking altogether.</summary>
    public void Stop() => _timer.Stop();

    /// <summary>Suppresses ticks without stopping the timer, for the length of a long operation.</summary>
    public void Pause() => _isPaused = true;

    /// <summary>Lets ticks through again.</summary>
    public void Resume() => _isPaused = false;
}
