using Avalonia;
using Microsoft.Extensions.Logging;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Utils;

namespace AvaloniaTest;

/// <summary>
/// Avalonia host test entry point. The composition root of this host is unusual for Avalonia: the GL
/// instance cannot be created here (Avalonia owns the context and hands it out per control), so the
/// window/GL half of the composition root lives in <see cref="Controls.RobotViewportControl"/>.
/// Everything else — the native import runtime, logging and the <c>--smoke</c> switch — is set up here,
/// before the UI starts. Which test data is loaded is not a command-line decision: the files are named
/// in the scene builder, exactly as in the bare-window host, so both hosts run the same data set.
/// </summary>
internal static class Program
{
    /// <summary>Frames rendered by <c>--smoke</c> when no count is given (same default in both hosts).</summary>
    private const int DefaultSmokeFrames = 120;

    // Smoke state lives here because this class is the composition root: it parses the switch, and it is
    // the last thing to run, so it is also the only place that can turn "what happened" into an exit code.
    private static long _smokeFrames = -1;
    private static long _framesRendered;
    private static bool _smokeCompleted;

    /// <summary>
    /// Frames a <c>--smoke</c> run must render before exiting, or <c>-1</c> when not in smoke mode. The
    /// viewport control (the render thread) reads it to decide whether the run has to end by itself; the
    /// bare-window host has the same switch and the same default, so one CI script drives either host.
    /// </summary>
    internal static long SmokeFrames => _smokeFrames;

    // Standard Avalonia template, except that the exit code is propagated: a `--smoke` run must be
    // watchable by a build script instead of a human looking at the window.
    [STAThread]
    public static int Main(string[] args)
    {
        // Assimp's native runtime must exist before any mesh import (URDF meshes are loaded later,
        // from inside the viewport control), and the host owns the logging destinations.
        AssimpNative.EnsureRuntime();
        Logger.Initialize(builder => builder.AddSimpleConsole());

        ParseArguments(args);

        // The arguments are consumed above and deliberately not forwarded: Avalonia's desktop lifetime
        // would parse them as its own, and `--smoke` is ours, not its.
        int exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
        return VerifySmoke(exitCode);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// <c>--smoke [frames]</c> switches to the CI smoke mode; there is no other switch (the bare-window
    /// host has the identical parser). Unknown arguments are reported and ignored.
    /// </summary>
    private static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--smoke")
            {
                _smokeFrames = DefaultSmokeFrames;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int frames) && frames > 0)
                {
                    _smokeFrames = frames;
                    i++;
                }
            }
            else
            {
                Logger.Warning($"Ignoring argument '{args[i]}'. Usage: [--smoke [frames]].");
            }
        }
    }

    /// <summary>
    /// Called once by the window when the smoke frame target has been reached, carrying the renderer's own
    /// frame count. Kept here (instead of in the control) because the count is only meaningful together
    /// with the requested count and the exit code.
    /// </summary>
    internal static void CompleteSmoke(long frames)
    {
        _framesRendered = frames;
        _smokeCompleted = true;
    }

    /// <summary>
    /// Turns the desktop lifetime's exit code into the process exit code: a smoke run counts as
    /// successful only when it rendered every requested frame, so a window closed early (or a control
    /// that failed to initialize) fails even though Avalonia reported success.
    /// </summary>
    private static int VerifySmoke(int exitCode)
    {
        if (_smokeFrames <= 0)
            return exitCode;

        if (exitCode != 0)
        {
            Logger.Error($"Smoke failed: the application exited with code {exitCode}.");
            return 1;
        }

        if (!_smokeCompleted || _framesRendered < _smokeFrames)
        {
            Logger.Error($"Smoke failed: rendered {_framesRendered} of {_smokeFrames} frame(s).");
            return 1;
        }

        return 0;
    }
}
