using System;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
using RobotSimulation.Robot;
using Silk.NET.OpenGL;

namespace AvaloniaTest.Controls;

/// <summary>
/// RobotViewport-style control: the single UI type an Avalonia application needs in order to embed
/// the library. Everything that differs between the bare-window / WPF / Avalonia hosts is confined
/// to this class — "how to obtain <c>GL</c> and how to sync the viewport" — while the rest of the
/// chain (context → renderer → scene) is identical everywhere.
/// <para>
/// Responsibilities, deliberately kept to three:
/// <list type="number">
/// <item>GL lifecycle: Avalonia hands out a context per control, so this is the composition root here.</item>
/// <item>Per-frame orchestration: bind the host framebuffer, sync the viewport, render, request the next frame.</item>
/// <item>Pointer input → camera (rviz-style rotate / pan / zoom / pick).</item>
/// </list>
/// </para>
/// <para>
/// What it renders is the library's whole feature set in miniature and nothing more: one URDF robot in a
/// default <see cref="SceneGraph"/>, which the user can orbit and pick (a click highlights the link under
/// the cursor). The camera pose is never written here — the <see cref="SceneGraph"/> constructor already
/// assembles a usable default scene and camera — so this class is the smallest complete Avalonia embed.
/// </para>
/// </summary>
public class RobotViewportControl : OpenGlControlBase
{
    // ------------------------------------------------------------------
    // Test data (edit only this block)
    //
    // The same single file as src/BareWindowTest/Program.cs: the two hosts are at their most useful when
    // they load the same file, so their console output can be compared line by line. The path is relative
    // to this project's Assets folder, which the project file copies next to the executable, and it is
    // written into the code on purpose — there is no path argument on the command line, so a run can never
    // silently test something other than the checked-in data.
    // See Assets/Models/README.md for where the file comes from.
    // ------------------------------------------------------------------

    /// <summary>Folder copied next to the executable that holds the test data.</summary>
    private const string AssetsRelativePath = "Assets";

    /// <summary>The one URDF model every run loads, relative to <see cref="AssetsRelativePath"/>.</summary>
    private const string ModelRelativePath = "Models/fairino3_v6/fairino3_v6.urdf";

    /// <summary>
    /// How often the GPU / frame-rate line is written to the console log (seconds). Matches the
    /// bare-window host, so a run of either host logs the same kind of line at the same rate.
    /// </summary>
    private const double StatsLogIntervalSeconds = 1.0;

    // rviz-style pointer sensitivities: rotate is degrees per pixel, pan is a target-displacement ratio.
    private const float RotateDegreesPerPixel = 0.2f;
    private const float PanScale = 0.01f;
    private const float ZoomPerDragPixel = 0.03f;
    private const float ZoomPerScrollNotch = 0.5f;
    private const double ClickDragThresholdPixels = 6.0;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // The viewport holds interfaces only; the concrete backend lives inside the GL callbacks.
    private GL? _gl;
    private IRenderContext? _graphics;
    private IRenderer? _renderer;
    private SceneGraph? _scene;
    private GameObject? _selected;

    private string? _initError;
    private TimeSpan _lastFrameTime;
    private double _statsLogAccumulator;
    private bool _smokeCompleted;

    // Pointer state (tracked on the window; see OnAttachedToVisualTree).
    private bool _dragging;
    private bool _dragged;
    private Point _pressPosition;
    private Point _lastPosition;

    /// <summary>Raised on the UI thread with a short user-facing line (e.g. the pick result).</summary>
    public event Action<string>? Message;

    /// <summary>Raised on the UI thread when the GL context could not be initialized.</summary>
    public event Action<string>? InitializationFailed;

    /// <summary>
    /// Raised on the UI thread once a <c>--smoke</c> run has rendered the requested frame count,
    /// carrying that exact count. The control stays out of application lifetime: the window decides
    /// what to do (log + close).
    /// </summary>
    public event Action<long>? SmokeCompleted;

    /// <summary>
    /// Loads the model named at the top of this file into <paramref name="scene"/>. It stays at the world
    /// origin: the library's scenes are Z-up and the robot's own base link sits at z=0, so the default
    /// camera looks straight at it without any placement code. A missing file is reported and skipped —
    /// a host never fails because of test data — and the logged wording is identical to the bare-window
    /// host's, which is what makes the two console logs diffable line by line.
    /// </summary>
    private static void LoadRobot(SceneGraph scene)
    {
        Logger.Info($"Test data: {Path.GetFullPath(AssetsRelativePath, AppContext.BaseDirectory)}");

        if (ResolveAssetFile(ModelRelativePath) is not { } modelPath)
            return;

        Logger.Info($"Loading model: {modelPath}");
        scene.Add(RobotModel.ParseFile(modelPath));
    }

    /// <summary>
    /// Absolute path of a file below this project's <c>Assets</c> folder, or null when it is absent (the
    /// caller then skips it). Resolved against <see cref="AppContext.BaseDirectory"/> because the project
    /// file copies <c>Assets</c> next to the executable, so the working directory a user happened to start
    /// the host from cannot change what is loaded.
    /// </summary>
    private static string? ResolveAssetFile(string relativePath)
    {
        string fullPath = Path.GetFullPath(relativePath, Path.Combine(AppContext.BaseDirectory, AssetsRelativePath));
        if (File.Exists(fullPath))
            return fullPath;

        Logger.Warning($"Test data file not found, skipping it: {fullPath}");
        return null;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            // Avalonia exposes raw proc addresses; wrapping them in a Silk facade is the entire
            // "how to get GL" difference of this host. GL stays inside this method afterwards.
            _gl = GL.GetApi(gl.GetProcAddress);

            // The backend can only deliver what it ships: desktop-GL shaders (#version 330 core). An
            // ES context (ANGLE / EGL on some platforms) would blow up deep inside shader
            // compilation, so say it plainly here instead.
            if (GlVersion.Type == GlProfileType.OpenGLES)
                throw new NotSupportedException(
                    $"the OpenGL backend needs a desktop GL context, but this control got GLES ({GlVersion}).");

            (_graphics, _renderer) = GraphicsFactory.Create(_gl);

            // The GPU line is written here, with identical wording to the bare-window host, so the two
            // runs can be compared line by line by a script instead of by a human looking at pixels.
            GraphicsDeviceInfo device = _graphics.DeviceInfo;
            Logger.Info($"GPU: {device.Renderer} | vendor: {device.Vendor} | " +
                        $"GL: {device.ApiVersion} | GLSL: {device.ShaderVersion}");

            // The scene is assembled exactly as in the bare-window host: a default SceneGraph (grid,
            // lights, world axes and a usable camera pose all come from its constructor) plus the one
            // URDF robot — only the way GL was obtained above differs between the two hosts.
            _scene = new SceneGraph();
            LoadRobot(_scene);
        }
        catch (Exception ex)
        {
            // A failure here must not take the whole application down: remember the reason, report it,
            // and skip rendering for the rest of this control's life.
            _initError = ex.Message;
            Logger.Error(ex);
            Dispatcher.UIThread.Post(() => InitializationFailed?.Invoke(_initError));
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_gl is null || _graphics is null || _renderer is null || _scene is null)
            return; // Initialization failed (already reported) — there is nothing to draw.

        // Avalonia renders this control into its own framebuffer; drawing into the default one (0)
        // would never reach the screen. The library never binds a framebuffer, so the host stays in
        // charge of the render target — which is exactly what makes it embeddable.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);

        // The viewport must follow the control size in *physical* pixels, otherwise the image is
        // blurred on HiDPI screens. The host owns this sync; the library only stores what it is told.
        double scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)(Bounds.Width * scaling));
        int pixelHeight = Math.Max(1, (int)(Bounds.Height * scaling));
        _graphics.Resize(pixelWidth, pixelHeight);
        _scene.Camera.AspectRatio = pixelWidth / (float)pixelHeight;

        TimeSpan now = _clock.Elapsed;
        double deltaSeconds = Math.Clamp((now - _lastFrameTime).TotalSeconds, 0, 0.25);
        _lastFrameTime = now;

        // Nothing in this host animates: the frame loop exists to prove that update → clear → render
        // runs every frame and that the GPU stays healthy while the user drives the camera.
        _scene.Update(deltaSeconds);
        _graphics.Clear(_scene.BackgroundColor); // Clear applies the scene's background color first.
        _renderer.Render(_scene);

        // Without this the image would freeze after the first frame.
        RequestNextFrameRendering();

        // Console log parity with the bare-window host: the same frame-rate line, once per second.
        FrameStats stats = _renderer.Stats; // pure data — safe to read from the render thread
        _statsLogAccumulator += deltaSeconds;
        if (_statsLogAccumulator >= StatsLogIntervalSeconds)
        {
            _statsLogAccumulator = 0;
            Logger.Info($"FPS {stats.Fps:F1} | last {stats.LastFrameMilliseconds:F2} ms | " +
                        $"avg {stats.AverageFrameMilliseconds:F2} ms | frames {stats.FrameCount}");
        }

        // Smoke mode is judged on the render thread against the renderer's own frame counter, so the
        // reported count is exact. The switch itself was parsed once in Program, by the same parser the
        // bare-window host uses.
        if (Program.SmokeFrames > 0 && !_smokeCompleted && stats.FrameCount >= Program.SmokeFrames)
        {
            _smokeCompleted = true; // the window closes once, exactly one report is made
            long rendered = stats.FrameCount;
            Dispatcher.UIThread.Post(() => SmokeCompleted?.Invoke(rendered));
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // Release GPU resources while the context is still current, then the GL facade itself.
        _renderer?.Dispose();
        _scene?.Dispose();
        _graphics?.Dispose();
        _gl?.Dispose();

        _renderer = null;
        _scene = null;
        _graphics = null;
        _gl = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Avalonia composites the GL control's content as a surface, so hit testing passes straight
        // through the control to its parent: the control would never see a pointer event on its own.
        // Listen on the window instead and filter by this control's bounds.
        if (VisualRoot is InputElement root)
        {
            root.AddHandler(PointerPressedEvent, OnWindowPointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerMovedEvent, OnWindowPointerMoved,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerReleasedEvent, OnWindowPointerReleased,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerWheelChangedEvent, OnWindowPointerWheel,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    /// <summary>Whether the pointer is over the control's rectangle (the window-level handlers see every event).</summary>
    private bool IsPointerInside(PointerEventArgs e)
    {
        Point position = e.GetPosition(this);
        return position.X >= 0 && position.Y >= 0 && position.X <= Bounds.Width && position.Y <= Bounds.Height;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scene is null || !IsPointerInside(e))
            return;

        _dragging = true;
        _dragged = false;
        _pressPosition = _lastPosition = e.GetPosition(this);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _scene is null)
            return;

        Point position = e.GetPosition(this);
        if (Distance(position, _pressPosition) > ClickDragThresholdPixels)
            _dragged = true; // A clear drag happened → the release must not be treated as a pick.

        var delta = new Vector2((float)(position.X - _lastPosition.X), (float)(position.Y - _lastPosition.Y));
        _lastPosition = position;

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            // Screen Y grows downward while camera pitch grows upward: negate dy or dragging is inverted.
            _scene.Camera.Rotate(-delta.X * RotateDegreesPerPixel, -delta.Y * RotateDegreesPerPixel);
        else if (point.Properties.IsMiddleButtonPressed)
            _scene.Camera.Pan(delta * PanScale);
        else if (point.Properties.IsRightButtonPressed)
            _scene.Camera.Zoom(-delta.Y * ZoomPerDragPixel); // Drag up (dy<0) → zoom in.

        RequestNextFrameRendering();
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;

        // A click (press + release without moving) selects; a drag only orbits the camera.
        if (!_dragged && _scene is not null && IsPointerInside(e))
            PickAt(e.GetPosition(this));
    }

    private void OnWindowPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_scene is null || !IsPointerInside(e))
            return;

        _scene.Camera.Zoom((float)e.Delta.Y * ZoomPerScrollNotch);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    /// <summary>
    /// Screen pixel → world ray → pick and single-select with highlight. The camera is pure CPU data,
    /// so this ray-cast runs without touching GL at all.
    /// </summary>
    private void PickAt(Point position)
    {
        SceneGraph scene = _scene!;
        Ray ray = scene.Camera.ScreenToWorldRay(
            new Vector2((float)position.X, (float)position.Y),
            new Vector2((float)Bounds.Width, (float)Bounds.Height));

        GameObject? picked = scene.PickAndHighlight(ray, enable: true);

        // Single selection: a new hit replaces the previous one, a miss clears it.
        if (_selected is { } previous && !ReferenceEquals(previous, picked))
            previous.Highlighted = false;
        if (picked is null && _selected is not null)
            _selected.Highlighted = false;
        _selected = picked;

        string message = picked is null ? "Pick: nothing selected" : $"Pick: selected '{picked.Name}'";
        Logger.Info(message);
        Dispatcher.UIThread.Post(() => Message?.Invoke(message));
        RequestNextFrameRendering();
    }

    /// <summary>Pixel distance between two points (the click / drag classifier).</summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
