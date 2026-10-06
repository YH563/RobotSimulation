using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.Robot;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace BareWindowTest;

/// <summary>
/// Bare-window host test: the smallest possible embedder, with no UI framework involved at all.
/// It proves that the library renders standalone from nothing but "a window and a GL context",
/// and it doubles as the automated smoke test:
/// <code>
///   BareWindowTest [--smoke [frames]]
/// </code>
/// Its behaviour is the library's whole feature set in miniature, and deliberately nothing more:
/// create a scene, load one URDF robot into it, then let the user orbit the camera (left-drag rotate,
/// middle-drag pan, right-drag or the wheel zoom) and pick (a click — press + release without moving —
/// highlights the link under the cursor). The camera pose is never written here:
/// <see cref="SceneGraph"/>'s constructor already assembles a usable default scene and camera, which is
/// exactly the behaviour an embedder should be able to rely on.
/// The loaded file is named in the source (see <see cref="ModelRelativePath"/>) instead of being passed
/// on the command line, so what a run loads is described entirely by the code and the two hosts can be
/// compared line by line; it lives in this project's own <c>Assets</c> folder, which the project file
/// copies next to the executable.
/// In smoke mode it renders the given number of frames (default 120) and then exits with code 0 on
/// success or 1 on failure, so CI can watch the process exit code instead of a human looking at pixels.
/// The host difference against the Avalonia host is only "how to get <c>GL</c> and how to sync the
/// viewport"; the rest of the chain (context → renderer → scene) is identical in both hosts.
/// </summary>
public static class Program
{
    private const string WindowTitle = "RobotSimulation - Bare Window Host";

    // ------------------------------------------------------------------
    // Test data (edit only this block)
    //
    // The path is relative to this project's Assets folder and uses '/' so it reads the same on every
    // platform; it is resolved against AppContext.BaseDirectory, where the build copied the folder.
    // A single URDF is enough to take the whole pipeline end to end — XML parsing, package:// asset
    // resolution, STL import, the link/joint tree and its materials — while keeping the host readable:
    //   * a real 6-axis robot — fairino3_v6 (URDF + 7 STL meshes)
    // See Assets/Models/README.md for where the file comes from.
    // ------------------------------------------------------------------

    /// <summary>Folder copied next to the executable that holds the test data.</summary>
    private const string AssetsRelativePath = "Assets";

    /// <summary>The one URDF model every run loads, relative to <see cref="AssetsRelativePath"/>.</summary>
    private const string ModelRelativePath = "Models/fairino3_v6/fairino3_v6.urdf";

    /// <summary>Frames rendered by <c>--smoke</c> when no count is given.</summary>
    private const int DefaultSmokeFrames = 120;

    /// <summary>How often the frame-rate interface is reported to the console (seconds).</summary>
    private const double StatsIntervalSeconds = 1.0;

    // rviz-style pointer sensitivities, byte-for-byte the same constants as the Avalonia host so a drag
    // feels identical in both: rotate is degrees per pixel, pan is a target-displacement ratio.
    private const float RotateDegreesPerPixel = 0.2f;
    private const float PanScale = 0.01f;
    private const float ZoomPerDragPixel = 0.03f;
    private const float ZoomPerScrollNotch = 0.5f;
    private const double ClickDragThresholdPixels = 6.0;

    private static IWindow _window = null!;
    private static IInputContext _input = null!;

    // The host holds interfaces only; the concrete backend (GraphicsContext/Renderer) is created
    // exclusively inside the OnLoad composition root and never leaves it.
    private static IRenderContext _graphics = null!;
    private static IRenderer _renderer = null!;
    private static SceneGraph _scene = null!;

    // The framebuffer GL actually draws into, in pixels. Silk reports it separately from the window size, and
    // on a scaled display the two are not the same rectangle. The renderer, the camera aspect and the pick ray
    // all describe this one size (see SyncViewport), so the picture and the ray cannot disagree about where a
    // pixel is.
    private static Vector2D<int> _pixelViewport;

    // Smoke mode: > 0 means "render this many frames, then close the window".
    private static long _framesToRender = -1;
    private static long _framesRendered;
    private static bool _failed;

    private static double _statsAccumulator;

    // Pointer state: the button that started the current gesture (null when idle), whether the pointer has
    // moved far enough for the release to count as a drag, the press position (click/drag classifier and
    // the origin of every drag displacement) and the camera pose captured at press time. The same fields
    // carry the same meaning in the Avalonia host: a click must not move the camera, because the pick is
    // only correct against the frame the user clicked on.
    private static MouseButton? _dragButton;
    private static bool _dragged;
    private static Vector2 _pressPosition;
    private static CameraPose _pressCamera;

    public static int Main(string[] args)
    {
        // The Core library attaches no logging providers itself, so the host owns the destinations. Mesh
        // import needs no runtime preparation: the Assimp native library ships per-RID with the bindings.
        Logger.Initialize(builder => builder.AddSimpleConsole());

        ParseArguments(args);

        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1280, 720),
            Title = WindowTitle,
            VSync = true
        };

        try
        {
            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Render += OnRender;
            _window.Resize += OnResize;
            _window.FramebufferResize += OnResize;
            _window.Closing += OnClosing;

            if (_framesToRender > 0)
                Logger.Info($"Smoke mode: rendering {_framesToRender} frame(s), then exiting.");

            _window.Run();
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            _failed = true;
        }

        if (_framesToRender > 0 && !_failed && _framesRendered < _framesToRender)
        {
            Logger.Error($"Smoke failed: rendered {_framesRendered} of {_framesToRender} frame(s).");
            _failed = true;
        }

        return _failed ? 1 : 0;
    }

    /// <summary>
    /// <c>--smoke [frames]</c> switches to the CI smoke mode; there is no other switch. Which file is
    /// loaded is a property of the code (see <see cref="ModelRelativePath"/>), not of the command line,
    /// so a run can never silently test something other than the checked-in data. Unknown arguments
    /// are reported and ignored instead of changing what is loaded.
    /// </summary>
    private static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--smoke")
            {
                _framesToRender = DefaultSmokeFrames;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int frames) && frames > 0)
                {
                    _framesToRender = frames;
                    i++;
                }
            }
            else
            {
                Logger.Warning($"Ignoring argument '{args[i]}'. Usage: [--smoke [frames]].");
            }
        }
    }

    private static void OnLoad()
    {
        // This is the only place the host touches GL; from here on everything goes through interfaces.
        GL gl = _window.CreateOpenGL();
        (_graphics, _renderer) = GraphicsFactory.Create(gl);

        // Device/driver info is pure data (no GL types), so it can be logged or bound to UI anywhere.
        GraphicsDeviceInfo gpu = _graphics.DeviceInfo;
        Logger.Info($"GPU: {gpu.Renderer} | vendor: {gpu.Vendor} | GL: {gpu.ApiVersion} | GLSL: {gpu.ShaderVersion}");

        // The default environment (grid floor / lights / world axes / camera pose) is assembled by the
        // SceneGraph constructor, so a bare window already shows a usable scene. The camera is left as
        // it comes: the default pose frames a single table-top-sized robot well, and overriding it here
        // would only hide what the library already does for a new embedder.
        _scene = new SceneGraph();
        _graphics.Resized += (width, height) => _scene.Camera.AspectRatio = width / (float)height;
        SyncViewport();

        LoadRobot(_scene);

        _input = _window.CreateInput();
        foreach (IKeyboard keyboard in _input.Keyboards)
            keyboard.KeyDown += OnKeyDown;

        // Pointer input → camera, exactly as in the Avalonia host. There the GL control does not receive
        // pointer events on its own and the handlers live on the window; here Silk delivers them already
        // window-scoped, so the two hosts differ only in how the events are hooked up, not in behaviour.
        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    /// <summary>
    /// Loads the model named at the top of this file into <paramref name="scene"/>. It stays at the world
    /// origin: the library's scenes are Z-up and the robot's own base link sits at z=0, so the default
    /// camera looks straight at it without any placement code. A missing file is reported and skipped —
    /// a host never fails because of test data — and the logged wording is identical to the Avalonia
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

    private static void OnRender(double deltaTime)
    {
        // Nothing in this host animates: the frame loop exists to prove that update → clear → render
        // runs every frame and that the GPU stays healthy while the user drives the camera.
        _scene.Update(deltaTime);
        _graphics.Clear(_scene.BackgroundColor);
        _renderer.Render(_scene);

        // Report the frame-rate interface (IRenderer.Stats) about once per second.
        _statsAccumulator += deltaTime;
        if (_statsAccumulator >= StatsIntervalSeconds)
        {
            _statsAccumulator = 0;
            FrameStats stats = _renderer.Stats;
            Logger.Info($"FPS {stats.Fps:F1} | last {stats.LastFrameMilliseconds:F2} ms | " +
                        $"avg {stats.AverageFrameMilliseconds:F2} ms | frames {stats.FrameCount}");
        }

        if (_framesToRender > 0 && ++_framesRendered >= _framesToRender)
        {
            Logger.Info($"Smoke OK: rendered {_framesRendered} frame(s) with no error.");
            _window.Close();
        }
    }

    private static void OnResize(Vector2D<int> size) => SyncViewport();

    /// <summary>
    /// Syncs the viewport to the size of the framebuffer GL is rendering into and remembers it for picking —
    /// the bare-window counterpart of the Avalonia host's viewport control. Silk reports the framebuffer size
    /// separately from the window size ("may differ from the window size"), and on a scaled display the two
    /// are indeed different rectangles: a host that draws into, or casts rays through, the window size while
    /// the picture fills the framebuffer is off by that ratio, further the closer the cursor is to the edge of
    /// the picture. The size is logged once per change, so the two numbers are on the record instead of being
    /// assumed equal.
    /// </summary>
    private static void SyncViewport()
    {
        Vector2D<int> surface = _window.FramebufferSize;
        if (surface.X <= 0 || surface.Y <= 0)
            surface = _window.Size; // Some backends report no framebuffer size; then the window size is all we have.

        _graphics.Resize(surface.X, surface.Y);
        _scene.Camera.AspectRatio = surface.X / (float)surface.Y;

        if (surface.X == _pixelViewport.X && surface.Y == _pixelViewport.Y)
            return;

        _pixelViewport = surface;
        Vector2D<int> windowSize = _window.Size;
        Logger.Info($"Viewport: {surface.X}x{surface.Y} px from framebuffer | window " +
                    $"{windowSize.X}x{windowSize.Y} px, scale " +
                    $"{surface.X / (float)Math.Max(1, windowSize.X):F4} px/unit");
    }

    /// <summary>
    /// Window pointer coordinates (what the input backend reports) → framebuffer pixels. The two spaces
    /// coincide only when the framebuffer happens to be the same size as the window; going through the
    /// measured ratio is what keeps the ray on the pixel the user is looking at.
    /// </summary>
    private static Vector2 PixelsFromWindowPoint(Vector2 position)
    {
        Vector2D<int> windowSize = _window.Size;
        if (windowSize.X <= 0 || windowSize.Y <= 0)
            return position;

        return new Vector2(
            position.X * (_pixelViewport.X / (float)windowSize.X),
            position.Y * (_pixelViewport.Y / (float)windowSize.Y));
    }

    // ------------------------------------------------------------------
    // Pointer input → camera (rviz-style rotate / pan / zoom / pick)
    //
    // Same gestures, same sensitivities and same log wording as the Avalonia host's viewport control, so
    // the two hosts behave identically to a human while their console output stays diffable by a script.
    // ------------------------------------------------------------------

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right) || _scene is null)
            return;

        _dragButton = button;
        _dragged = false;
        _pressPosition = mouse.Position;
        _pressCamera = CameraPose.Capture(_scene.Camera); // A click has to keep the camera exactly here.
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_dragButton is not { } button || _scene is null)
            return;

        Vector2 total = position - _pressPosition;

        // Inside the threshold (the pointer jitter of an ordinary click) the camera is deliberately left
        // alone: the pose captured at press time is the one the release will pick through. Only a clear
        // drag may move it.
        if (!_dragged && total.Length() > ClickDragThresholdPixels)
            _dragged = true;

        if (!_dragged)
            return;

        // A drag is applied as the *total* displacement from the press point on top of the press-time pose
        // instead of per-event deltas: the camera cannot jump when the threshold is crossed and rounding
        // cannot accumulate into a slow drift away from the cursor.
        Camera camera = _scene.Camera;
        _pressCamera.Restore(camera);

        switch (button)
        {
            case MouseButton.Left:
                // Screen Y grows downward while camera pitch grows upward: negate dy or dragging is inverted.
                camera.Rotate(-total.X * RotateDegreesPerPixel, -total.Y * RotateDegreesPerPixel);
                break;
            case MouseButton.Middle:
                camera.Pan(total * PanScale);
                break;
            case MouseButton.Right:
                camera.Zoom(-total.Y * ZoomPerDragPixel); // Drag up (dy<0) → zoom in.
                break;
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (_dragButton != button)
            return;

        _dragButton = null;

        // A click (press + release without moving) selects; a drag only orbits the camera. The pointer may
        // still have jittered inside the threshold, so the camera is put back exactly where it was when the
        // button went down: the ray is then cast through the picture the user clicked on.
        if (_dragged || _scene is null)
            return;

        _pressCamera.Restore(_scene.Camera);
        PickAt(mouse.Position);
    }

    private static void OnMouseScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_scene is null)
            return;

        _scene.Camera.Zoom(wheel.Y * ZoomPerScrollNotch); // Scroll up (y>0) → zoom in.
    }

    /// <summary>
    /// Screen pixel → world ray → pick and single-select with highlight. The camera is pure CPU data, so
    /// this ray-cast runs without touching GL at all.
    /// </summary>
    private static void PickAt(Vector2 position)
    {
        SceneGraph scene = _scene!;

        // The pointer arrives in window coordinates while the picture lives in the framebuffer: converting
        // through the measured ratio (see SyncViewport) is what makes the ray go through the pixel the user
        // sees, whatever the display scale does to the two sizes.
        Ray ray = scene.Camera.ScreenToWorldRay(
            PixelsFromWindowPoint(position), new Vector2(_pixelViewport.X, _pixelViewport.Y));

        // Single selection with feedback: the scene owns the selection, so a new hit replaces the previous one (its
        // highlight and its local axes go away) and a miss clears it. The axes it mounts on the hit are the same
        // marker the corner gizmo shows, but for the picked node's own frame.
        GameObject? picked = scene.PickAndSelect(ray);

        Logger.Info(picked is null ? "Pick: nothing selected" : $"Pick: selected '{picked.Name}'");
    }

    /// <summary>
    /// The orbit state a click is required to preserve: captured when the button goes down and put back
    /// before the ray is cast, because picking is only meaningful against the frame the user clicked on.
    /// The Avalonia host carries the same type, so both hosts classify and apply pointer input identically.
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

    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
            _window.Close();
    }

    private static void OnClosing()
    {
        _input?.Dispose();
        _renderer?.Dispose(); // GPU resources are released while the context is still alive.
        _scene?.Dispose();
        _graphics?.Dispose();
    }
}
