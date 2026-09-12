using System;
using System.Diagnostics;
using System.Threading;

namespace RobotSimulation.Core.Utils;

/// <summary>
/// A standalone timer that fires <see cref="Tick"/> at a fixed frame rate on a background thread.
/// </summary>
public class GameTimer : IDisposable
{
    private readonly Stopwatch _stopwatch = new();
    private Timer? _timer;
    private readonly object _lock = new();
    private bool _disposed = false;

    // Target frame rate, default 60 fps.
    /// <summary>Target frame rate in hertz (default 60); changing it takes effect on the next <see cref="Start"/>.</summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>Whether the timer is currently ticking.</summary>
    public bool IsRunning { get; private set; } = false;

    /// <summary>Raised once per tick on a background thread; the argument is the elapsed time in seconds (clamped to 0.1).</summary>
    public event Action<float>? Tick;

    /// <summary>Creates a stopped timer; call <see cref="Start"/> to begin ticking.</summary>
    public GameTimer() { }

    /// <summary>
    /// Starts the timer.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;
            IsRunning = true;
            _stopwatch.Restart();

            _timer = new Timer(
                _ => OnTick(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1000.0 / TargetFps)
            );
        }
    }

    /// <summary>
    /// Stops the timer (no more Tick events).
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnTick()
    {
        if (_disposed) return;
        if (!IsRunning) return;

        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        // Clamp the delta to avoid physics explosions (max 0.1 s).
        float delta = (float)Math.Min(elapsed, 0.1);

        // Fired on a background thread; never manipulate OpenGL here.
        Tick?.Invoke(delta);
    }

    /// <summary>Stops the timer and releases the underlying system timer; safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _timer?.Dispose();
        _disposed = true;
    }
}
