using System;
using System.Diagnostics;
using System.Threading;

namespace RobotSimulation.Core.Utils;

/// <summary>
/// 独立计时器
/// </summary>
public class GameTimer : IDisposable
{
    private readonly Stopwatch _stopwatch = new();
    private Timer? _timer;
    private readonly object _lock = new();
    private bool _disposed = false;
    
    // 目标帧率，默认60帧
    public int TargetFps { get; set; } = 60;
    public bool IsRunning { get; private set; } = false;
    
    // 每次 Update 触发的事件，参数为增量时间（秒）
    public event Action<float>? Tick;
    
    public GameTimer(){}
    
    /// <summary>
    /// 启动计时器
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
    /// 停止计时器（不再触发 Tick）
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

        // 钳制增量防止物理爆炸（最大 0.1 秒）
        float delta = (float)Math.Min(elapsed, 0.1);

        // 在后台线程触发，禁止操作 OpenGL
        Tick?.Invoke(delta);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _timer?.Dispose();
        _disposed = true;
    }
}