namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Frame timing statistics reported by a renderer. Pure data (no graphics-API types), so any backend
/// can produce it and any host (Avalonia / WPF / bare window) can display it — the library never draws
/// an on-screen overlay itself.
/// </summary>
/// <param name="Fps">Smoothed frames per second (based on the interval between render calls).</param>
/// <param name="LastFrameMilliseconds">Wall-clock time of the most recent frame, in milliseconds.</param>
/// <param name="AverageFrameMilliseconds">Smoothed average frame time, in milliseconds.</param>
/// <param name="FrameCount">Total number of frames rendered since the renderer was created.</param>
public readonly record struct FrameStats(
    double Fps,
    double LastFrameMilliseconds,
    double AverageFrameMilliseconds,
    long FrameCount)
{
    /// <summary>Zeroed statistics (used before the first frame has been rendered).</summary>
    public static readonly FrameStats Empty = default;
}
