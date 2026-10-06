using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RobotSimulation.Core.Utils;

namespace AvaloniaTest.Views;

/// <summary>
/// Application window: it owns the application lifetime and relays whatever the viewport reports.
/// The library ships no HUD of its own — GPU and frame-rate data are printed to the console with exactly
/// the lines the bare-window host prints — so the window keeps only the pointer hints and the latest
/// user-facing message (e.g. the pick result). That is what makes a run of either host comparable: the
/// console carries the testable output, the window carries the interactive one.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Viewport.Message += message => HintText.Text = message;
        Viewport.InitializationFailed += OnInitializationFailed;
        Viewport.SmokeCompleted += OnSmokeCompleted;

        // The command line was parsed once in Program, by the same parser the bare-window host uses.
        if (Program.SmokeFrames > 0)
            Logger.Info($"Smoke mode: rendering {Program.SmokeFrames} frame(s), then exiting.");
    }

    /// <summary>
    /// Ends a <c>--smoke</c> run: the viewport reports the exact frame count the renderer produced, and
    /// the window (which owns the application lifetime) closes with exit code 0.
    /// </summary>
    private void OnSmokeCompleted(long frames)
    {
        Program.CompleteSmoke(frames);
        Logger.Info($"Smoke OK: rendered {frames} frame(s) with no error.");
        Shutdown(0);
    }

    /// <summary>
    /// A GL initialization failure is fatal for a smoke run (there would be nothing to verify) and
    /// harmless otherwise: the control simply never draws, and the console log carries the reason.
    /// </summary>
    private void OnInitializationFailed(string error)
    {
        Logger.Error($"OpenGL initialization failed: {error}");

        if (Program.SmokeFrames > 0)
            Shutdown(1);
    }

    /// <summary>Ends the desktop lifetime with the given process exit code.</summary>
    private void Shutdown(int exitCode)
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.Shutdown(exitCode);
}
