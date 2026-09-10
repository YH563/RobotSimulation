using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
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
/// All test data comes from this project's own <c>Assets</c> folder, which the project file copies
/// next to the executable; the relative paths are written into the source (see
/// <see cref="ModelRelativePaths"/>) instead of being passed on the command line, so what a run loads
/// is described entirely by the code and two hosts can be compared line by line. A single run loads
/// every kind of test data at once — URDF built-in geometry, a community URDF package, a real robot,
/// every supported mesh format, and RGB point clouds.
/// The window is interactive exactly like the Avalonia host: left-drag orbits the camera, middle-drag
/// pans, right-drag or the wheel zooms, and a click (press + release without moving) picks and highlights
/// the object under the cursor. Same sensitivities, same wiring rules, same log wording as that host.
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
    // Paths are relative to this project's Assets folder and use '/' so they read the same on every
    // platform; they are resolved against AppContext.BaseDirectory, where the build copied the folder.
    // Every entry is loaded by every run, so one smoke run covers all of it:
    //   * URDF built-in geometry    — primitives.urdf (box / sphere / cylinder / capsule)
    //   * community URDF package    — urdf_tutorial (the ROS tutorial package, DAE + STL-less meshes)
    //   * a real robot              — fairino3_v6 (URDF + STL meshes)
    //   * every mesh format         — formats.urdf (STL / OBJ+MTL / DAE / glTF / PLY in one tree)
    //   * RGB point clouds          — a PLY with red/green/blue channels and a PCD with packed rgb
    // See Assets/Models/README.md and Assets/PointClouds/README.md for where the files come from.
    // ------------------------------------------------------------------

    /// <summary>Folder copied next to the executable that holds all test data.</summary>
    private const string AssetsRelativePath = "Assets";

    /// <summary>URDF models loaded by every run, in load order, relative to <see cref="AssetsRelativePath"/>.</summary>
    private static readonly string[] ModelRelativePaths =
    {
        "Models/primitives.urdf",                      // URDF built-in geometry
        "Models/urdf_tutorial/02-multipleshapes.urdf", // community package (ROS urdf_tutorial)
        "Models/urdf_tutorial/05-visual.urdf",         // community package, DAE meshes + textures
        "Models/fairino3_v6/fairino3_v6.urdf",         // a real 6-axis robot (STL meshes)
        "Models/formats/formats.urdf",                 // STL / OBJ+MTL / DAE / glTF / PLY
    };

    /// <summary>Point clouds loaded by every run, in load order, relative to <see cref="AssetsRelativePath"/>.</summary>
    private static readonly string[] PointCloudRelativePaths =
    {
        "PointClouds/rgb_cloud.ply", // PLY ascii, separate red/green/blue channels
        "PointClouds/rgb_cloud.pcd", // PCD ascii, packed rgb channel
    };

    /// <summary>Horizontal distance between the loaded models, so the whole data set is visible at once.</summary>
    private const float ModelSpacing = 2.0f;

    /// <summary>Point clouds float above the models at this height, this far apart.</summary>
    private const float CloudHeight = 1.8f;
    private const float CloudSpacing = 2.5f;

    /// <summary>Frames rendered by <c>--smoke</c> when no count is given.</summary>
    private const int DefaultSmokeFrames = 120;

    /// <summary>Rotation speed of the demo box (degrees per second), used only to prove the frame loop runs.</summary>
    private const double SpinDegreesPerSecond = 45.0;

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
    private static GameObject? _spinner;

    private static double _spinDegrees;
    private static double _statsAccumulator;

    // Pointer state: the button that started the current drag (null when idle), whether the pointer has
    // moved far enough for the release to count as a drag, and the two positions used for the delta and
    // the click/drag classifier. The same four fields carry the same meaning in the Avalonia host.
    private static MouseButton? _dragButton;
    private static bool _dragged;
    private static Vector2 _pressPosition;
    private static Vector2 _lastPosition;

    /// <summary>The currently picked object (single selection: a new pick replaces it, a miss clears it).</summary>
    private static GameObject? _selected;

    // Smoke mode: > 0 means "render this many frames, then close the window".
    private static long _framesToRender = -1;
    private static long _framesRendered;
    private static bool _failed;

    public static int Main(string[] args)
    {
        // Prepare the Assimp native runtime before any mesh import; the Core library attaches no
        // logging providers itself, so the host owns the destinations.
        AssimpNative.EnsureRuntime();
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
    /// <c>--smoke [frames]</c> switches to the CI smoke mode; there is no other switch. Which files are
    /// loaded is a property of the code (see <see cref="ModelRelativePaths"/>), not of the command line,
    /// so a run can never silently test something other than the checked-in data set. Unknown arguments
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
        // SceneGraph constructor, so a bare window already shows a usable scene.
        _scene = new SceneGraph();
        _graphics.Resized += (width, height) => _scene.Camera.AspectRatio = width / (float)height;
        _graphics.Resize(_window.Size.X, _window.Size.Y);

        // Camera: pulled back far enough that the whole test data set — models spread along X, point
        // clouds floating above them — is inside the frame from the very first frame.
        _scene.Camera.Target = new Vector3(0f, 0.9f, 0f);
        _scene.Camera.Distance = 13f;

        // Loads the whole test data set and hands back the object the frame loop animates.
        _spinner = LoadTestData(_scene);

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
    /// Loads the test data listed at the top of this file into <paramref name="scene"/> and returns the
    /// object the frame loop animates. The data is laid out so nothing overlaps: models along X, point
    /// clouds floating above them, and a spinning box in front of the camera (without motion a frozen
    /// frame and a live one look identical). A missing file is reported and skipped — a host never fails
    /// because of test data — and every loaded file is logged with the same wording as the other host, so
    /// two runs can be diffed directly. This method and the two helpers below are intentionally identical
    /// in the Avalonia host, which is what makes that diff meaningful.
    /// </summary>
    private static GameObject LoadTestData(SceneGraph scene)
    {
        Logger.Info($"Test data: {Path.GetFullPath(AssetsRelativePath, AppContext.BaseDirectory)}");

        for (int i = 0; i < ModelRelativePaths.Length; i++)
        {
            if (ResolveAssetFile(ModelRelativePaths[i]) is not { } modelPath)
                continue;

            Logger.Info($"Loading model: {modelPath}");
            RobotModel model = RobotModel.ParseFile(modelPath);
            // A robot's link transforms are a read-only subtree by design, so the whole robot is placed
            // through its built-in RootPose interface rather than by writing Transform.Position.
            model.RootPose = Matrix4x4.CreateTranslation(
                (i - (ModelRelativePaths.Length - 1) * 0.5f) * ModelSpacing, 0f, 0f);
            scene.Add(model);
        }

        for (int i = 0; i < PointCloudRelativePaths.Length; i++)
        {
            if (ResolveAssetFile(PointCloudRelativePaths[i]) is not { } cloudPath)
                continue;

            PointCloud cloud = PointCloud.FromFile(cloudPath);
            // The point count and the decoded colour of the first point prove that the file *and* its
            // colour channel were understood — something a screenshot cannot tell a CI script.
            PointCloud2Data? cloudData = cloud.PointData; // FromFile always attaches data; null is a bug
            Logger.Info($"Loading point cloud: {cloudPath} ({cloudData?.Count ?? 0} points, " +
                        $"color: {DescribeColor(cloudData)})");
            cloud.Transform.Position = new Vector3(
                (i - (PointCloudRelativePaths.Length - 1) * 0.5f) * CloudSpacing, CloudHeight, 0.6f);
            scene.Add(cloud);
        }

        var box = new Box(0.6f, 0.6f, 0.6f, "smoke-box");
        box.MaterialData!.BaseColor = new Vector4(0.9f, 0.55f, 0.2f, 1f);
        box.Transform.Position = new Vector3(0f, 0.35f, 1.8f);
        scene.Add(box);
        return box;
    }

    /// <summary>
    /// Absolute path of a file below this project's <c>Assets</c> folder, or null when it is absent (the
    /// caller then skips that entry). Resolved against <see cref="AppContext.BaseDirectory"/> because the
    /// project file copies <c>Assets</c> next to the executable, so the working directory a user happened
    /// to start the host from cannot change what is loaded.
    /// </summary>
    private static string? ResolveAssetFile(string relativePath)
    {
        string fullPath = Path.GetFullPath(relativePath, Path.Combine(AppContext.BaseDirectory, AssetsRelativePath));
        if (File.Exists(fullPath))
            return fullPath;

        Logger.Warning($"Test data file not found, skipping it: {fullPath}");
        return null;
    }

    /// <summary>
    /// One-line description of the colours a cloud carries: the first point's rgba, or <c>none</c> when
    /// the file has no colour channel at all. Reading the colour back is the only way to check that the
    /// per-point channels were decoded rather than ignored in favour of the material colour.
    /// </summary>
    private static string DescribeColor(PointCloud2Data? data)
    {
        if (data is not { HasColor: true, Count: > 0 })
            return "none";

        Vector4 first = data.GetColor(0);
        return $"first point rgba({first.X:F2}, {first.Y:F2}, {first.Z:F2})";
    }

    private static void OnRender(double deltaTime)
    {
        // Spin the demo box (degrees → quaternion), proving the per-frame update → render chain runs.
        if (_spinner is { } box)
        {
            _spinDegrees += deltaTime * SpinDegreesPerSecond;
            box.Transform.Rotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY, (float)(_spinDegrees * Math.PI / 180.0));
        }

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

    private static void OnResize(Vector2D<int> size)
    {
        _graphics.Resize(size.X, size.Y);
        _scene.Camera.AspectRatio = size.X / (float)size.Y;
    }

    // ------------------------------------------------------------------
    // Pointer input → camera (rviz-style rotate / pan / zoom / pick)
    //
    // Same gestures, same sensitivities and same log wording as the Avalonia host's viewport control, so
    // the two hosts behave identically to a human while their console output stays diffable by a script.
    // ------------------------------------------------------------------

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right))
            return;

        _dragButton = button;
        _dragged = false;
        _pressPosition = _lastPosition = mouse.Position;
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_dragButton is not { } button || _scene is null)
            return;

        Vector2 delta = position - _lastPosition;
        _lastPosition = position;

        // A clear drag happened → the release must not be treated as a pick.
        if (Distance(position, _pressPosition) > ClickDragThresholdPixels)
            _dragged = true;

        switch (button)
        {
            case MouseButton.Left:
                // Screen Y grows downward while camera pitch grows upward: negate dy or dragging is inverted.
                _scene.Camera.Rotate(-delta.X * RotateDegreesPerPixel, -delta.Y * RotateDegreesPerPixel);
                break;
            case MouseButton.Middle:
                _scene.Camera.Pan(delta * PanScale);
                break;
            case MouseButton.Right:
                _scene.Camera.Zoom(-delta.Y * ZoomPerDragPixel); // Drag up (dy<0) → zoom in.
                break;
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (_dragButton != button)
            return;

        _dragButton = null;

        // A click (press + release without moving) selects; a drag only orbits the camera.
        if (!_dragged)
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
        Ray ray = scene.Camera.ScreenToWorldRay(position, new Vector2(_window.Size.X, _window.Size.Y));

        GameObject? picked = scene.PickAndHighlight(ray, enable: true);

        // Single selection: a new hit replaces the previous one, a miss clears it.
        if (_selected is { } previous && !ReferenceEquals(previous, picked))
            previous.Highlighted = false;
        if (picked is null && _selected is not null)
            _selected.Highlighted = false;
        _selected = picked;

        Logger.Info(picked is null ? "Pick: nothing selected" : $"Pick: selected '{picked.Name}'");
    }

    /// <summary>Pixel distance between two points (the click / drag classifier).</summary>
    private static double Distance(Vector2 a, Vector2 b)
        => (a - b).Length();

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