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

    /// <summary>
    /// Frame on which a smoke run performs the pick self-check: late enough for the window to have been laid
    /// out at its final size, early enough to keep a smoke run short.
    /// </summary>
    private const int PickCheckFrame = 10;

    /// <summary>Surface samples per link the pick self-check projects (closest to the camera first).</summary>
    private const int PickCheckSamplesPerLink = 3;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // The viewport holds interfaces only; the concrete backend lives inside the GL callbacks.
    private GL? _gl;
    private IRenderContext? _graphics;
    private IRenderer? _renderer;
    private SceneGraph? _scene;

    private string? _initError;
    private TimeSpan _lastFrameTime;
    private double _statsLogAccumulator;
    private bool _smokeCompleted;

    // Pointer state (tracked on the window; see OnAttachedToVisualTree).
    //
    // A gesture stays a click until the pointer leaves ClickDragThresholdPixels, and a click must not move
    // the camera at all: the ray is cast against the frame the user actually saw, so a few pixels of
    // press/release jitter would otherwise resolve against a camera pose the user never looked through —
    // the usual reason a click that looks like it is on a link selects nothing.
    private bool _dragging;
    private bool _dragged;
    private Point _pressPosition;
    private CameraPose _pressCamera;

    // Size the control was last arranged at: a resize has to produce a frame of its own (see ArrangeOverride).
    private Size _arrangedSize;

    // Pixel rectangle the last rendered frame went into. It is *measured* from GL (see SyncViewport) rather
    // than derived from Bounds × RenderScaling, and it is the single number the renderer, the camera aspect
    // and the pick ray all agree on. Guessing it instead is the classic embedded-viewport bug: the compositor
    // rounds on fractional display scales (Windows at 125% / 150%), the guess rounds differently, and every
    // click then lands a little off the cursor — more so the bigger the control is.
    private (int Width, int Height) _pixelViewport;

    /// <summary>Where <see cref="_pixelViewport"/> came from (diagnostics: the viewport log line names it).</summary>
    private string _viewportSource = "not measured yet";

    /// <summary>Guards the one-shot pick self-check (see RunPickCheck).</summary>
    private bool _pickCheckDone;

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

        // The scene belongs to the thread that constructed it (OnOpenGlInit above) and is driven here. Both are
        // Avalonia's GL-control thread, so this is normally a no-op — but which thread a GL control initialises and
        // renders on is the framework's business, not this host's contract with the library, and declaring the
        // render thread as the owner is exactly the host's job. Doing it before the first frame boundary keeps a
        // different framework choice from turning into "the boundary runs on a thread that does not own the scene".
        // On the owner thread it does nothing.
        if (!_scene.IsOwnerThread)
            _scene.ClaimOwnership();

        // Avalonia renders this control into its own framebuffer; drawing into the default one (0)
        // would never reach the screen. The library never binds a framebuffer, so the host stays in
        // charge of the render target — which is exactly what makes it embeddable.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);

        // The viewport is the one authoritative answer to "where is a pixel": the compositor draws this
        // control's content into the framebuffer bound above, so the renderer and the pick ray must both
        // describe that very rectangle. The host owns this sync — the library only stores what it is told —
        // and the same numbers are reused by RayAt, so the two can never disagree.
        (int pixelWidth, int pixelHeight) = SyncViewport();
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

        // A smoke run also exercises the host's own coordinate conversion once, on the real framebuffer size
        // and display scale of the machine it runs on (see RunPickCheck). The headless tests cover the
        // library's math; only a running host can show that the pointer's space, the compositor's surface and
        // the ray agree *here*.
        if (Program.SmokeFrames > 0 && !_pickCheckDone && stats.FrameCount >= PickCheckFrame)
        {
            _pickCheckDone = true;
            (bool ok, string report) = RunPickCheck();
            if (ok)
                Logger.Info(report);
            else
            {
                Logger.Error(report);
                Program.ReportPickCheckFailure(report);
            }
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
        _pressPosition = e.GetPosition(this);
        _pressCamera = CameraPose.Capture(_scene.Camera); // A click has to keep the camera exactly here.

        LogPointerMapping(e, _pressPosition);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _scene is null)
            return;

        Point position = e.GetPosition(this);

        // Inside the threshold (the pointer jitter of an ordinary click) the camera is deliberately left
        // alone: the pose captured at press time is the one the pick has to use, because that is the pose
        // the user was looking through. Only a clear drag may move it.
        if (!_dragged && Distance(position, _pressPosition) > ClickDragThresholdPixels)
            _dragged = true; // A clear drag happened → the release must not be treated as a pick.

        if (!_dragged)
            return;

        // A drag is applied as the *total* displacement from the press point on top of the pose captured at
        // press time instead of per-event deltas: the camera cannot jump when the threshold is crossed and
        // rounding cannot accumulate into a slow drift away from the cursor.
        var total = new Vector2(
            (float)(position.X - _pressPosition.X),
            (float)(position.Y - _pressPosition.Y));

        Camera camera = _scene.Camera;
        _pressCamera.Restore(camera);

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            // Screen Y grows downward while camera pitch grows upward: negate dy or dragging is inverted.
            camera.Rotate(-total.X * RotateDegreesPerPixel, -total.Y * RotateDegreesPerPixel);
        else if (point.Properties.IsMiddleButtonPressed)
            camera.Pan(total * PanScale);
        else if (point.Properties.IsRightButtonPressed)
            camera.Zoom(-total.Y * ZoomPerDragPixel); // Drag up (dy<0) → zoom in.

        RequestNextFrameRendering();
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;

        // A click (press + release without moving) selects; a drag only orbits the camera.
        if (_dragged || _scene is null || !IsPointerInside(e))
            return;

        // The pointer may still have jittered by a pixel or two without leaving the click threshold, so
        // the camera is put back exactly where it was when the button went down: the ray is then cast
        // through the picture the user clicked on, not through a pose they never saw.
        _pressCamera.Restore(_scene.Camera);
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

    /// <summary>World point → pixel (top-left origin), the exact inverse of <see cref="Camera.ScreenToWorldRay"/>.</summary>
    private static Vector2 ProjectToPixel(Vector3 world, Camera camera, Vector2 viewport)
    {
        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        float inverseW = 1f / clip.W;

        return new Vector2(
            (clip.X * inverseW + 1f) * 0.5f * viewport.X,
            (1f - clip.Y * inverseW) * 0.5f * viewport.Y);
    }

    /// <summary>
    /// A few front-facing surface points of <paramref name="node"/> that land inside the viewport, nearest to
    /// the camera first — the pixels a user would click to select this link.
    /// </summary>
    private static IEnumerable<Vector3> SampleVisibleSurface(GameObject node, SceneGraph scene, Vector2 viewport)
    {
        MeshData mesh = node.MeshData!;
        Matrix4x4 model = node.Transform.GetModelMatrix();
        Vector3 eye = scene.Camera.Position;
        var candidates = new List<(float Distance, Vector3 Point)>();

        // A stride keeps the scan proportional to the sample count, not to the mesh size.
        int stride = Math.Max(1, mesh.TriangleCount / 4000);
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3 * stride)
        {
            Vector3 a = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i]], model);
            Vector3 b = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i + 1]], model);
            Vector3 c = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i + 2]], model);

            Vector3 center = (a + b + c) / 3f;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), center - eye) >= 0f)
                continue; // Back-facing: the camera cannot see this triangle.

            Vector2 pixel = ProjectToPixel(center, scene.Camera, viewport);
            if (pixel.X < 1f || pixel.Y < 1f || pixel.X > viewport.X - 1f || pixel.Y > viewport.Y - 1f)
                continue; // Off-screen.

            candidates.Add((Vector3.Distance(center, eye), center));
        }

        candidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
        for (int i = 0; i < Math.Min(PickCheckSamplesPerLink, candidates.Count); i++)
            yield return candidates[i].Point;
    }

    /// <summary>The meshes a click can select: the same filter the picker applies (pickable, visible, real geometry).</summary>
    private static IEnumerable<GameObject> EnumeratePickable(IReadOnlyList<GameObject> roots)
    {
        foreach (GameObject root in roots)
            foreach (GameObject node in EnumerateSubtree(root))
                if (node.Pickable && node.Visible && node.MeshData is { TriangleCount: > 0 })
                    yield return node;
    }

    private static IEnumerable<GameObject> EnumerateSubtree(GameObject node)
    {
        yield return node;
        foreach (Transform child in node.Transform.Children)
            foreach (GameObject descendant in EnumerateSubtree(child.Owner))
                yield return descendant;
    }

    /// <summary>
    /// Screen pixel → world ray → pick and single-select with highlight. The camera is pure CPU data,
    /// so this ray-cast runs without touching GL at all.
    /// </summary>
    private void PickAt(Point position)
    {
        SceneGraph scene = _scene!;

        // Single selection with feedback: the scene owns the selection, so a new hit replaces the previous one (its
        // highlight and its local axes go away) and a miss clears it. The axes it mounts on the hit are the same
        // marker the corner gizmo shows, but for the picked node's own frame.
        GameObject? picked = scene.PickAndSelect(RayAt(position));

        string message = picked is null ? "Pick: nothing selected" : $"Pick: selected '{picked.Name}'";
        Logger.Info(message);
        Dispatcher.UIThread.Post(() => Message?.Invoke(message));
        RequestNextFrameRendering();
    }

    /// <summary>
    /// The pick ray for a position in this control's *own* layout coordinates — exactly what
    /// <c>PointerEventArgs.GetPosition(this)</c> returns, and the only space in which the pointer, the layout
    /// and the framebuffer can be reconciled. Both conversions (layout units → framebuffer pixels and pixels
    /// → NDC) happen here and nowhere else, so the click path and the self-check (see RunPickCheck) cannot
    /// disagree about where the cursor is.
    /// </summary>
    private Ray RayAt(Point position)
    {
        SceneGraph scene = _scene!;
        (int width, int height) = PixelViewport;
        Vector2 scale = PixelsPerLayoutUnit;

        // A pick can arrive between a resize and the next rendered frame; refreshing the aspect from the same
        // numbers the viewport is bound with keeps the ray in step with the picture it is cast through.
        scene.Camera.AspectRatio = width / (float)height;

        return scene.Camera.ScreenToWorldRay(
            new Vector2((float)(position.X * scale.X), (float)(position.Y * scale.Y)),
            new Vector2(width, height));
    }

    /// <summary>
    /// The pixel rectangle the current frame is drawn into: what GL reported, or the control's own layout
    /// size until the first frame has been rendered (see <see cref="SyncViewport"/>).
    /// </summary>
    private (int Width, int Height) PixelViewport =>
        _pixelViewport.Width > 0 ? _pixelViewport : LayoutPixelSize();

    /// <summary>
    /// Device-independent (layout) → physical pixel factor of the window this control is rendered into.
    /// Only a prediction, used before the first frame; afterwards the measured
    /// <see cref="PixelsPerLayoutUnit"/> is what the pointer goes through. It is deliberately not trusted for
    /// picking: on Avalonia 12 the compositor handed this control a 1157x755 px surface for a 1100x718 layout
    /// while this property reported 1.00, so it cannot be the factor that describes where a pixel is.
    /// </summary>
    private double RenderScaling => (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;

    /// <summary>
    /// Physical pixels per layout unit: how far the pointer (always in layout units) has to travel to cover
    /// one pixel of the framebuffer. Taken from the rectangle actually in use rather than from
    /// <see cref="RenderScaling"/> alone, so the rounding the compositor applies on fractional display scales
    /// (Windows at 125% / 150%) cannot shift the ray by a pixel or two — an error that grows with the control
    /// and shows up as clicks that slide off a link.
    /// </summary>
    private Vector2 PixelsPerLayoutUnit
    {
        get
        {
            (int width, int height) = PixelViewport;
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return new Vector2((float)RenderScaling, (float)RenderScaling);

            return new Vector2(width / (float)Bounds.Width, height / (float)Bounds.Height);
        }
    }

    /// <summary>The control's layout size in physical pixels: the prediction used until GL is asked.</summary>
    private (int Width, int Height) LayoutPixelSize() => (
        Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling)),
        Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling)));

    /// <summary>
    /// Measures the framebuffer the frame is about to be drawn into, binds it as the GL viewport and
    /// remembers it for <see cref="RayAt"/>. GL is asked first, because the compositor — not this control —
    /// decides how large that surface is; only when the query yields nothing (a default framebuffer, a driver
    /// that hides the attachment) does the control fall back to predicting the size from its own layout.
    /// Either way the renderer and the ray use the same rectangle, so the picture and the pick can never
    /// disagree about where a pixel is. A change of size is logged once, with its source, so that a mismatch
    /// between the compositor's rounding and the layout's is visible instead of being guessed at.
    /// </summary>
    private (int Width, int Height) SyncViewport()
    {
        (int width, int height) = QueryFramebufferSize();
        string source = "framebuffer";
        if (width <= 0 || height <= 0)
        {
            (width, height) = LayoutPixelSize();
            source = "control layout × scaling";
        }

        _gl!.Viewport(0, 0, (uint)width, (uint)height);

        if ((width, height) != _pixelViewport)
        {
            (int layoutWidth, int layoutHeight) = LayoutPixelSize();
            _pixelViewport = (width, height);
            _viewportSource = source;
            Logger.Info($"Viewport: {width}x{height} px from {source} | control layout " +
                        $"{Bounds.Width:F0}x{Bounds.Height:F0} at scaling {RenderScaling:F2} " +
                        $"= {layoutWidth}x{layoutHeight} px");
        }

        return (width, height);
    }

    /// <summary>
    /// Size of the colour attachment of the bound framebuffer, straight from GL — that is the surface the
    /// compositor samples afterwards, so it defines the pixel grid the picture lives on.
    /// <c>(0, 0)</c> when the attachment is neither a texture nor a renderbuffer, which is the one case (a
    /// default framebuffer) the caller has to predict instead.
    /// </summary>
    private (int Width, int Height) QueryFramebufferSize()
    {
        GL gl = _gl!;

        gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferAttachmentParameterName.ObjectType, out int attachmentType);

        if (attachmentType == (int)GLEnum.Renderbuffer)
        {
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectName, out int renderbuffer);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)renderbuffer);
            gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                RenderbufferParameterName.Width, out int bufferWidth);
            gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                RenderbufferParameterName.Height, out int bufferHeight);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            return (bufferWidth, bufferHeight);
        }

        if (attachmentType == (int)GLEnum.Texture)
        {
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectName, out int texture);
            gl.BindTexture(TextureTarget.Texture2D, (uint)texture);
            gl.GetTexLevelParameter(TextureTarget.Texture2D, 0, GLEnum.TextureWidth, out int textureWidth);
            gl.GetTexLevelParameter(TextureTarget.Texture2D, 0, GLEnum.TextureHeight, out int textureHeight);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            return (textureWidth, textureHeight);
        }

        return (0, 0);
    }

    /// <summary>
    /// A resize must produce a frame of its own: until then the compositor keeps showing the previous
    /// picture (stretched, and with the previous camera aspect), so a click arriving right after a resize
    /// would be resolved against a view the user is no longer looking at.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);

        if (_scene is not null && arranged != _arrangedSize)
        {
            _arrangedSize = arranged;
            RequestNextFrameRendering();
        }

        return arranged;
    }

    /// <summary>Pixel distance between two points (the click / drag classifier).</summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The orbit state a click is required to preserve: captured when the button goes down and put back
    /// before the ray is cast, because picking is only meaningful against the frame the user clicked on.
    /// </summary>
    private readonly record struct CameraPose(float Yaw, float Pitch, float Distance, Vector3 Target)
    {
        public static CameraPose Capture(Camera camera)
            => new(camera.Yaw, camera.Pitch, camera.Distance, camera.Target);

        public void Restore(Camera camera)
        {
            camera.Target = Target;
            camera.Distance = Distance;
            camera.Yaw = Yaw;
            camera.Pitch = Pitch;
        }
    }

    /// <summary>
    /// One line that makes a coordinate problem impossible to miss, printed when the button goes down: the
    /// pointer in this control's own space, the same point in the window's space, and where this control
    /// starts in that space. The first two must differ by exactly the third — which is what
    /// <c>GetPosition(this)</c> already takes care of, and exactly what a host that picked with window
    /// coordinates would skip, shifting every click by the control's offset in the window (the classic "the
    /// click lands on another part of the robot than the cursor" bug of an embedded viewport). The measured
    /// pixel scale and viewport follow on the same line, so a disagreement between the compositor's surface
    /// and the control's layout is on the record too.
    /// </summary>
    private void LogPointerMapping(PointerEventArgs e, Point controlPosition)
    {
        if (VisualRoot is not Visual root)
            return;

        Point windowPosition = e.GetPosition(root);
        Point origin = this.TranslatePoint(default, root) ?? default;
        Vector2 scale = PixelsPerLayoutUnit;
        (int width, int height) = PixelViewport;

        Logger.Info(
            $"Pointer: control={controlPosition.X:F1},{controlPosition.Y:F1} " +
            $"window={windowPosition.X:F1},{windowPosition.Y:F1} " +
            $"control-origin={origin.X:F1},{origin.Y:F1} " +
            $"bounds={Bounds.Width:F0}x{Bounds.Height:F0} scale={scale.X:F4},{scale.Y:F4} px/unit " +
            $"viewport={width}x{height} ({_viewportSource}) " +
            $"pixel={controlPosition.X * scale.X:F1},{controlPosition.Y * scale.Y:F1}");
    }

    /// <summary>
    /// Round-trips the whole click chain inside the running host: a point on a link's visible surface is
    /// projected to the pixel the compositor draws it on, that pixel is converted back into the control's
    /// layout coordinates through the very conversion a click uses (<see cref="RayAt"/>), and the pick is
    /// asked what lies under it. Every sample has to resolve to the link it came from, and the point reported
    /// back has to project onto the pixel that was asked about.
    /// <para>
    /// This is the check no headless test can make, because it depends on what this machine did: the real
    /// framebuffer size, the real display scale and the real position of the control are all in play. A
    /// coordinate conversion that silently disagrees with the compositor fails here instead of being noticed
    /// only as "clicks sometimes miss a link".
    /// </para>
    /// </summary>
    private (bool Ok, string Report) RunPickCheck()
    {
        SceneGraph scene = _scene!;
        (int width, int height) = PixelViewport;
        var viewport = new Vector2(width, height);
        Vector2 pixelsPerUnit = PixelsPerLayoutUnit;

        int samples = 0;
        int reachedTheirLink = 0;
        int coveredByANearerLink = 0;
        int misses = 0;
        float worstPixelError = 0f;

        foreach (GameObject node in EnumeratePickable(scene.Roots))
        {
            foreach (Vector3 surfacePoint in SampleVisibleSurface(node, scene, viewport))
            {
                samples++;

                Vector2 pixel = ProjectToPixel(surfacePoint, scene.Camera, viewport);

                // The pixel the compositor draws that point on, expressed the way a pointer arrives: layout
                // units inside the control. This is the conversion a click goes through.
                var layoutPoint = new Point(pixel.X / pixelsPerUnit.X, pixel.Y / pixelsPerUnit.Y);

                RaycastHit? hit = scene.Pick(RayAt(layoutPoint));
                if (hit is null)
                {
                    // The picture shows a surface here, so a ray through the click must reach it.
                    misses++;
                    continue;
                }

                // The hit point has to sit on the pixel that was asked about: the "the ray matches the
                // picture" half of the correspondence.
                float error = Vector2.Distance(ProjectToPixel(hit.Value.Point, scene.Camera, viewport), pixel);
                worstPixelError = MathF.Max(worstPixelError, error);

                if (ReferenceEquals(hit.Value.Object, node))
                {
                    reachedTheirLink++;
                    continue;
                }

                // Another link owns this pixel: either a nearer one really does cover the sample (this is not
                // a visibility test), or two links of a URDF model interpenetrate around a joint. Both are
                // geometry, not a coordinate problem — the round-trip error is what judges the ray.
                float sampleDepth = Vector3.Distance(surfacePoint, scene.Camera.Position);
                if (hit.Value.Distance <= sampleDepth * 1.01f)
                    coveredByANearerLink++;
            }
        }

        // The size the control would have predicted for itself is named in the report: on a real Avalonia 12
        // window it is *smaller* than the surface the compositor hands out (1100x718 against 1157x755), and
        // that ratio is exactly what a host that trusts the prediction picks wrong by.
        (int predictedWidth, int predictedHeight) = LayoutPixelSize();
        string predictionNote = (predictedWidth, predictedHeight) == (width, height)
            ? "the layout prediction agrees"
            : $"the layout prediction is {predictedWidth}x{predictedHeight} px, " +
              $"{(float)width / predictedWidth:F4}× off";

        string summary =
            $"Pick check: {reachedTheirLink}/{samples} surface samples select their own link, " +
            $"{coveredByANearerLink} are covered by a nearer link, {misses} are missed entirely, " +
            $"worst round-trip error {worstPixelError:F2}px, viewport {width}x{height} px ({_viewportSource}), " +
            $"scale {pixelsPerUnit.X:F4},{pixelsPerUnit.Y:F4} px/unit ({predictionNote})";

        if (samples == 0)
            return (false, $"{summary} | no visible surface sample was found to check");

        // The picture and the ray describe the same rectangle only if a pixel projected from a surface point
        // comes back through the same rectangle: mixing the two spaces — layout units against framebuffer
        // pixels, or a viewport other than the one that was drawn into — shows up here as an error that grows
        // with the distance from the centre, which is what "clicks miss on a large window" means in numbers.
        if (worstPixelError > 1f)
            return (false, $"{summary} | the ray and the projection disagree about where a pixel is");

        // A ray aimed at a surface the picture shows has to reach it; a single sample can hide behind a
        // neighbour, so the bar for "the ray is where the cursor is" is deliberately low.
        if (misses > 0 || reachedTheirLink < Math.Max(1, samples / 4))
            return (false, $"{summary} | the ray does not reach the surfaces the picture shows");

        return (true, summary);
    }
}
