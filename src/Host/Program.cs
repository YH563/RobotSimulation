using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Microsoft.Extensions.Logging;
using System.Numerics;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
using RobotSimulation.Robot;
using RobotSimulation.Robot.Urdf;
using System.Buffers.Binary;
using System.IO;
using RobotSimulation.Core.Geometry;

namespace RobotSimulation;

public class Program
{
    private static IWindow _window = null!;

    // Use interface types externally; the concrete backend (GraphicsContext/Renderer) is created only in the OnLoad composition root.
    private static IRenderContext _graphics = null!;
    private static SceneGraph _scene = null!;
    private static GameTimer _gameTimer = null!;
    private static RobotModel? _robot;
    private static IRenderer _renderer = null!;
    private static string? _urdfPath;
    private static string? _cloudPath;

    // rviz-style mouse control parameters (if the direction/feel is off, change only these signs/sensitivities).
    private const float RotateSpeed = 0.2f; // Rotate: degrees per pixel.
    private const float PanScale = 0.01f; // Pan: target-displacement ratio.
    private const float ZoomDragSpeed = 0.03f; // Right-button vertical-drag zoom: distance per pixel.
    private const float ZoomScrollSpeed = 0.5f; // Scroll-wheel zoom: distance per tick.

    private static Vector2 _lastMousePos;
    private static bool _isDragging = false;

    // ---- Click picking (left click selects; dragging still rotates the view) ----
    private static Vector2 _mouseDownPos;         // Screen position when the left button was pressed.
    private static bool _didDrag;                 // Whether a clear drag happened after press (distinguishes click vs drag).
    private static GameObject? _selected;         // Currently selected object (single selection: a hit replaces, a miss clears).
    private const float ClickDragThreshold = 6f;  // Pixel displacement threshold to classify as a drag.

    public static void Main(string[] args)
    {
        // Prepare the Assimp native runtime (add a libdl compatibility link on Linux; must precede any mesh import call).
        AssimpNative.EnsureRuntime();
        // Log to the console (the Core library itself attaches no providers; the host owns destinations).
        Logger.Initialize(builder => builder.AddSimpleConsole());
        // URDF location: a CLI argument takes precedence, otherwise search recursively under the default Assets/Models (logic in Robot's UrdfLocator).
        _urdfPath = UrdfLocator.Find(args is { Length: > 0 } ? args[0] : null);
        _cloudPath = args is { Length: > 1 } ? args[1] : null;

        try
        {
            var options = WindowOptions.Default with
            {
                Size = new Vector2D<int>(1200, 800),
                Title = "Robot Simulation - URDF Demo",
                VSync = true
            };
            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Render += OnRender;
            _window.Closing += OnClosing;
            _window.Resize += OnResize;
            _window.Run();
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Console.ReadLine();
        }
    }

    private static void OnLoad()
    {
        // Rendering context: the GL instance is created only here in the host composition root and injected;
        // afterward it is used only through IRenderContext.
        GL gl = _window.CreateOpenGL(); // The only place the Host touches GL.
        // Concrete backend types appear only inside the factory; the host holds the interfaces only.
        (_graphics, _renderer) = GraphicsFactory.Create(gl);
        _scene = new SceneGraph();

        _graphics.Resized += (w, h) => _scene.Camera.AspectRatio = w / (float)h;
        _graphics.Resize(_window.Size.X, _window.Size.Y);

        // The default scene environment (grid floor / default lights / world axes / camera pose) is
        // assembled automatically by the SceneGraph constructor; no manual call is needed.

        // Renderer: GPU meshes/materials live only inside it (Scene/GameObject are pure data); the
        // standard pass shaders (model/line/point/skybox) are embedded in the OpenGL backend, so the
        // host needs no shader paths.

        // ---- URDF robot: load the CLI-specified or first .urdf under Assets/Models and display it ----
        if (_urdfPath is { } modelPath)
        {
            Logger.Info($"Loading model: {modelPath}");
            _robot = RobotModel.ParseFile(modelPath); // Path resolution uses Robot's default FileSystemAssetResolver (throws on missing).
            Logger.Info(
                $"'{_robot.RobotName}': links={_robot.Description.Links.Count}, joints={_robot.Description.Joints.Count}");
            _scene.Add(_robot); // Added to the scene like any other GameObject, drawn by the renderer.
        }
        else
        {
            Logger.Warning("No URDF specified. Usage: RobotSimulation.Host <model.urdf>, or drop a .urdf into Assets/Models/ and run.");
        }

        // ---- Minimal sample content: add a few visible objects (default grid/lights/world axes are auto-assembled by SceneGraph) ----
        var demoCurve = new List<Vector3>();
        for (int i = 0; i <= 64; i++)
        {
            float t = i / 64f * MathF.PI * 4f;
            demoCurve.Add(new Vector3(
                -1.2f + 0.5f * MathF.Cos(t),
                -1.2f + 0.5f * MathF.Sin(t),
                0.25f + 0.2f * MathF.Sin(t * 2f)));
        }

        _scene.Add(new Curve(demoCurve, new Vector4(0.2f, 0.8f, 1f, 1f), "demo-curve"));

        var demoArrow = new Arrow(0.5f, 0.015f, 0.05f, 0.12f, new Vector4(1f, 0.62f, 0.1f, 1f), 24, "demo-arrow");
        demoArrow.Transform.Position = new Vector3(-1.2f, 1.2f, 0f);
        _scene.Add(demoArrow);

        // ---- Point cloud demo (PointCloud2-style field layout + per-point colors) ----
        _scene.Add(BuildDemoCloud());

        // Optional: render a point cloud loaded from a file (second CLI argument, .pcd / .ply).
        if (_cloudPath is { } cloudPath)
        {
            if (File.Exists(cloudPath))
                _scene.Add(PointCloud.FromFile(cloudPath));
            else
                Logger.Warning($"Point cloud file not found, skipping: {cloudPath}");
        }

        var input = _window.CreateInput();
        foreach (var kb in input.Keyboards)
            kb.KeyDown += OnKeyDown;
        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }

        _gameTimer = new GameTimer();
        _gameTimer.Tick += OnGameTick;
        _gameTimer.Start();
    }

    private static void OnGameTick(float deltaTime)
    {
        _scene.Update(deltaTime);
    }

    private static void OnRender(double deltaTime)
    {
        // The model is static (no auto animation); drive it externally via robot.SetJointValue(...) when needed.

        _graphics.Clear(_scene.BackgroundColor); // Background color comes from the scene's display defaults (medium gray).
        _renderer.Render(_scene);
    }

    private static void OnResize(Vector2D<int> size)
    {
        _graphics.Resize(size.X, size.Y);
        _scene.Camera.AspectRatio = size.X / (float)size.Y;
    }

    private static void OnClosing()
    {
        _gameTimer?.Stop();
        _gameTimer?.Dispose();
        _renderer?.Dispose(); // GPU resources are released here.
        _scene?.Dispose();
        _graphics?.Dispose();
    }

    // ---- Input events (rviz style) ----
    //   Left-drag = rotate    Middle-drag = pan    Right-drag = zoom (up zooms in, down zooms out)    Scroll = zoom
    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
            _window.Close();
        else if (key == Key.W)
            _scene.Camera.Zoom(0.6f); // W = zoom in (backup for when no scroll wheel).
        else if (key == Key.S)
            _scene.Camera.Zoom(-0.6f); // S = zoom out.
    }

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left || button == MouseButton.Middle || button == MouseButton.Right)
        {
            _isDragging = true;
            _mouseDownPos = new Vector2(mouse.Position.X, mouse.Position.Y);
            _didDrag = false;
            _lastMousePos = _mouseDownPos;
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            // A click (no drag after press) → fire a ray pick and select; a drag → keep rotating, no pick.
            if (!_didDrag)
                TryPickAt(new Vector2(mouse.Position.X, mouse.Position.Y));
            _isDragging = false;
        }
        else if (button == MouseButton.Middle || button == MouseButton.Right)
        {
            _isDragging = false;
        }
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_isDragging) return;

        // If the accumulated displacement from the press point exceeds the threshold, classify as a "drag",
        // so the subsequent "click-to-pick" is not triggered.
        if ((position - _mouseDownPos).Length() > ClickDragThreshold)
            _didDrag = true;

        Vector2 delta = new Vector2(position.X, position.Y) - _lastMousePos;
        _lastMousePos = new Vector2(position.X, position.Y);

        if (mouse.IsButtonPressed(MouseButton.Left))
            // Negate the horizontal so the scene "follows" the mouse drag direction rather than scrolling backwards.
            _scene.Camera.Rotate(-delta.X * RotateSpeed, -delta.Y * RotateSpeed);
        else if (mouse.IsButtonPressed(MouseButton.Middle))
            _scene.Camera.Pan(delta * PanScale);
        else if (mouse.IsButtonPressed(MouseButton.Right))
            // Drag up (dy<0) → Zoom positive → distance decreases → zoom in.
            _scene.Camera.Zoom(-delta.Y * ZoomDragSpeed);
    }

    private static void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        float dy = scroll.Y != 0f ? scroll.Y : scroll.X; // Handle platforms that put vertical scroll on X.
        if (dy == 0f)
            return;

        _scene.Camera.Zoom(dy * ZoomScrollSpeed);
    }

    /// <summary>
    /// Screen pixel coordinates → world ray → pick and single-select with highlight. The selection state
    /// is updated regardless of a hit: on a new hit, clear the previous selection first and highlight the
    /// hit object; on a miss, clear the selection.
    /// </summary>
    private static void TryPickAt(Vector2 screenPos)
    {
        var ray = _scene.Camera.ScreenToWorldRay(screenPos, new Vector2(_window.Size.X, _window.Size.Y));
        GameObject? picked = _scene.PickAndHighlight(ray, enable: true);

        // Single-select replacement: if the hit differs from the last one, un-highlight the previous; if a
        // miss, clear the previous selection.
        if (_selected is { } prev && !ReferenceEquals(prev, picked))
            prev.Highlighted = false;
        if (picked is null && _selected is not null)
            _selected.Highlighted = false;
        _selected = picked;

        Logger.Info(picked is null ? "Pick: no hit" : $"Pick: selected '{picked.Name}'");
    }

    /// <summary>
    /// Builds a small colored point cloud to demonstrate the PointCloud2-style field layout
    /// (a hue rainbow mapped onto a sphere), with per-point RGB packed into a float.
    /// </summary>
    private static PointCloud BuildDemoCloud()
    {
        var fields = new[]
        {
            new PointField("x", 0, PointFieldDataType.Float32),
            new PointField("y", 4, PointFieldDataType.Float32),
            new PointField("z", 8, PointFieldDataType.Float32),
            new PointField("rgb", 12, PointFieldDataType.Float32),
        };

        const int n = 2000;
        var data = new byte[n * 16];
        float goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f)); // ≈2.39996 rad
        for (int i = 0; i < n; i++)
        {
            // Fibonacci sphere: deterministically spread n points uniformly over the unit sphere, not a spiral.
            float t = (i + 0.5f) / n;
            float y = 1f - 2f * t;                       // +1 → -1
            float r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
            float theta = goldenAngle * i;
            float x = MathF.Cos(theta) * r;
            float z = MathF.Sin(theta) * r;

            int off = i * 16;
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off), x);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off + 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off + 8), z);

            // Scale each channel to 0..255 then truncate (avoid (uint) truncating [0,1] to 0/1, which would make most points black).
            uint rgb = ((uint)((MathF.Sin(t * MathF.PI * 2f) * 0.5f + 0.5f) * 255f)) << 16
                | ((uint)((MathF.Sin(t * MathF.PI * 2f + 2.09f) * 0.5f + 0.5f) * 255f)) << 8
                | ((uint)((MathF.Sin(t * MathF.PI * 2f + 4.18f) * 0.5f + 0.5f) * 255f));

            // Pack RGB into a float whose bit pattern is 0x00RRGGBB.
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off + 12),
                BitConverter.Int32BitsToSingle((int)rgb));
        }

        var cloudData = new PointCloud2Data(fields, data, 16, width: n, height: 1);
        var cloud = new PointCloud(2f, null, "demo-pointcloud", cloudData);
        cloud.Transform.Position = new Vector3(1.2f, 1.2f, 0f);
        return cloud;
    }
}
