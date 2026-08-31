using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using System.Numerics;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;

namespace RobotSimulation;

public class Program
{
    private static IWindow _window;
    private static GraphicsContext _graphics;
    private static Scene _scene;
    private static GameTimer _gameTimer;
    private static Vector2 _lastMousePos;
    private static bool _isDragging = false;

    public static void Main(string[] args)
    {
        try
        {
            var options = WindowOptions.Default with
            {
                Size = new Vector2D<int>(1200, 800),
                Title = "Robot Simulation - Basic Cube",
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
            Console.WriteLine($"Unhandled exception: {ex}");
            Console.ReadLine();
        }
    }

    private static void OnLoad()
    {
        GL gl = _window.CreateOpenGL();
        _graphics = new GraphicsContext(gl);
        _scene = new Scene();

        _graphics.Resized += (w, h) => _scene.Camera.AspectRatio = w / (float)h;
        _graphics.Resize(_window.Size.X, _window.Size.Y);

        _scene.LightPosition = new Vector3(5, 8, 5);
        _scene.LightColor = new Vector3(1, 1, 1);

        var shader = _graphics.ShaderCache.GetOrCreate(
            "Assets/Shaders/Model/Standard.vert",
            "Assets/Shaders/Model/Standard.frag"
        );

        Mesh cubeMesh = CreateCubeMesh(gl);
        var material = new Material(shader);
        material.BaseColor = new Vector4(0.9f, 0.15f, 0.15f, 1.0f); // 红色立方体

        var cube = new GameObject(cubeMesh, material);
        cube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30f * MathUtils.DegToRad);
        _scene.Add(cube);

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
        _graphics.ClearColor(Color.CornflowerBlue);
        _graphics.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _scene.Render();
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
        _scene?.Dispose();
        _graphics?.Dispose();
    }

    // ---- 输入事件 ----
    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape) _window.Close();
    }

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left || button == MouseButton.Right)
        {
            _isDragging = true;
            _lastMousePos = new Vector2(mouse.Position.X, mouse.Position.Y);
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left || button == MouseButton.Right)
            _isDragging = false;
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_isDragging) return;
        Vector2 delta = new Vector2(position.X, position.Y) - _lastMousePos;
        _lastMousePos = new Vector2(position.X, position.Y);

        if (mouse.IsButtonPressed(MouseButton.Left))
            _scene.Camera.Rotate(delta.X * 0.2f, -delta.Y * 0.2f);
        else if (mouse.IsButtonPressed(MouseButton.Right))
            _scene.Camera.Pan(delta * 0.01f);
    }

    private static void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        _scene.Camera.Zoom(scroll.Y * 0.5f);
    }

    // ---- 创建立方体网格 ----
    private static Mesh CreateCubeMesh(GL gl)
    {
        float half = 0.5f;
        float[] vertices = {
            // 位置 (3) + UV (2) + 法线 (3) + 切线 (3)
            -half, -half,  half,  0f,0f,  0f,0f,1f,  1f,0f,0f,
             half, -half,  half,  1f,0f,  0f,0f,1f,  1f,0f,0f,
             half,  half,  half,  1f,1f,  0f,0f,1f,  1f,0f,0f,
            -half,  half,  half,  0f,1f,  0f,0f,1f,  1f,0f,0f,
            -half, -half, -half,  1f,0f,  0f,0f,-1f, -1f,0f,0f,
             half, -half, -half,  0f,0f,  0f,0f,-1f, -1f,0f,0f,
             half,  half, -half,  0f,1f,  0f,0f,-1f, -1f,0f,0f,
            -half,  half, -half,  1f,1f,  0f,0f,-1f, -1f,0f,0f,
            -half,  half, -half,  0f,0f,  0f,1f,0f,  1f,0f,0f,
             half,  half, -half,  1f,0f,  0f,1f,0f,  1f,0f,0f,
             half,  half,  half,  1f,1f,  0f,1f,0f,  1f,0f,0f,
            -half,  half,  half,  0f,1f,  0f,1f,0f,  1f,0f,0f,
            -half, -half, -half,  0f,1f,  0f,-1f,0f,  1f,0f,0f,
             half, -half, -half,  1f,1f,  0f,-1f,0f,  1f,0f,0f,
             half, -half,  half,  1f,0f,  0f,-1f,0f,  1f,0f,0f,
            -half, -half,  half,  0f,0f,  0f,-1f,0f,  1f,0f,0f,
             half, -half, -half,  0f,0f,  1f,0f,0f,  0f,0f,-1f,
             half, -half,  half,  1f,0f,  1f,0f,0f,  0f,0f,-1f,
             half,  half,  half,  1f,1f,  1f,0f,0f,  0f,0f,-1f,
             half,  half, -half,  0f,1f,  1f,0f,0f,  0f,0f,-1f,
            -half, -half, -half,  1f,0f, -1f,0f,0f,  0f,0f,1f,
            -half, -half,  half,  0f,0f, -1f,0f,0f,  0f,0f,1f,
            -half,  half,  half,  0f,1f, -1f,0f,0f,  0f,0f,1f,
            -half,  half, -half,  1f,1f, -1f,0f,0f,  0f,0f,1f
        };

        // 每个面的三角形顶点顺序必须保证从外侧看为逆时针(CCW)，
        // 否则会被背面剔除(CullFace.Back)当成背面剔除，导致该面在正面观察时消失。
        // 已用叉积逐面验证：front/bottom/left 正确；back/top/right 绕序已修正。
        uint[] indices = {
             0,1,2, 2,3,0,          // front (+Z)   CCW ✓
             4,6,5, 6,4,7,          // back  (-Z)   CCW ✓ (原 4,5,6/6,7,4 为反向绕序)
             8,10,9, 10,8,11,       // top   (+Y)   CCW ✓ (原 8,9,10/10,11,8 为反向绕序)
             12,13,14, 14,15,12,    // bottom(-Y)   CCW ✓
             16,18,17, 18,16,19,    // right (+X)   CCW ✓ (原 16,17,18/18,19,16 为反向绕序)
             20,21,22, 22,23,20     // left  (-X)   CCW ✓
        };

        return new Mesh(gl, vertices, indices);
    }
}