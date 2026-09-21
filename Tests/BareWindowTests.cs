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

namespace RobotSimulation.Tests;

public class BareWindowTests
{
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
    
    private const string WindowTitle = "RobotSimulation - Bare Window Host";
    
    private static IWindow _window = null!;
    private static IInputContext _input = null!;
    
    private static IRenderContext _graphics = null!;
    private static IRenderer _renderer = null!;
    private static SceneGraph _scene = null!;
    private static Vector2D<int> _pixelViewport;
    
    // 鼠标操作相关参数
    private const float RotateDegreesPerPixel = 0.2f;
    private const float PanScale = 0.01f;
    private const float ZoomPerDragPixel = 0.03f;
    private const float ZoomPerScrollNotch = 0.5f;
    private const double ClickDragThresholdPixels = 6.0;
    
    private static MouseButton? _dragButton;
    private static bool _dragged;
    private static Vector2 _pressPosition;
    private static CameraPose _pressCamera;
    
    [Fact]
    public void BareWindowTest()
    {
        CreateWindow();
    }

    private static void CreateWindow()
    {
        Logger.Initialize(builder => builder.AddSimpleConsole());
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
            _window.Run();
        }
        catch (Exception e)
        {
            Logger.Error(e);
            throw;
        }
    }
    
    private static void OnLoad()
    {
        // 获取GL上下文，创建渲染上下文以及渲染器
        GL gl = _window.CreateOpenGL();
        (_graphics, _renderer) = GraphicsFactory.Create(gl);
        
        // 获取GPU信息
        GraphicsDeviceInfo gpu = _graphics.DeviceInfo;
        Logger.Info($"GPU: {gpu.Renderer} | vendor: {gpu.Vendor} | GL: {gpu.ApiVersion} | GLSL: {gpu.ShaderVersion}");
        
        _scene = new SceneGraph();
        AddGameObjects(_scene);
        _graphics.Resized += (width, height) => _scene.Camera.AspectRatio = width / (float)height;
        SyncViewport();
        
        _input = _window.CreateInput();
        // 添加键盘操作
        foreach (IKeyboard keyboard in _input.Keyboards)
            keyboard.KeyDown += OnKeyDown;
        // 添加鼠标操作
        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    /// <summary>
    /// 添加GameObject对象
    /// </summary>
    /// <param name="scene"></param>
    private static void AddGameObjects(SceneGraph scene)
    {
        GameObject box1 = new Box(1, 1, 1, "BlueBox");
        box1.MaterialData!.BaseColor = new Vector4(0, 0, 1, 1);   // Box always supplies MaterialData.
        GameObject box2 = new Box(2, 2, 2, "RedBox");
        box2.MaterialData!.BaseColor = new Vector4(1, 0, 0, 1);
        box2.Transform.Position = new Vector3(3, 0, 0);
        scene.Add(box1);
        scene.Add(box2);
    }

    private static void OnRender(double deltaTime)
    {
        // 遍历场景内所有 gameobject 进行更新
        _scene.Update(deltaTime);
        _graphics.Clear(_scene.BackgroundColor);
        _renderer.Render(_scene);
    }
    
    private static void OnResize(Vector2D<int> size) => SyncViewport();
    
    /// <summary>
    /// 同步更新窗口尺寸
    /// </summary>
    private static void SyncViewport()
    {
        Vector2D<int> surface = _window.FramebufferSize;
        if (surface.X <= 0 || surface.Y <= 0)
            surface = _window.Size; 

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
    
    private static Vector2 PixelsFromWindowPoint(Vector2 position)
    {
        Vector2D<int> windowSize = _window.Size;
        if (windowSize.X <= 0 || windowSize.Y <= 0)
            return position;

        return new Vector2(
            position.X * (_pixelViewport.X / (float)windowSize.X),
            position.Y * (_pixelViewport.Y / (float)windowSize.Y));
    }
    
    /// <summary>
    /// 鼠标按下
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="button"></param>
    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button is not (MouseButton.Left or MouseButton.Middle or MouseButton.Right) || _scene is null)
            return;

        _dragButton = button;
        _dragged = false;
        _pressPosition = mouse.Position;
        _pressCamera = CameraPose.Capture(_scene.Camera); // A click has to keep the camera exactly here.
    }

    /// <summary>
    /// 鼠标移动
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="position"></param>
    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_dragButton is not { } button || _scene is null)
            return;

        Vector2 total = position - _pressPosition;
        
        if (!_dragged && total.Length() > ClickDragThresholdPixels)
            _dragged = true;

        if (!_dragged)
            return;
        
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

    /// <summary>
    /// 鼠标松开
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="button"></param>
    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (_dragButton != button)
            return;

        _dragButton = null;
        if (_dragged || _scene is null)
            return;

        _pressCamera.Restore(_scene.Camera);
        PickAt(mouse.Position);
    }

    /// <summary>
    /// 操作鼠标滚轮
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="wheel"></param>
    private static void OnMouseScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_scene is null)
            return;

        _scene.Camera.Zoom(wheel.Y * ZoomPerScrollNotch); // Scroll up (y>0) → zoom in.
    }
    
    private static void PickAt(Vector2 position)
    {
        SceneGraph scene = _scene!;
        Ray ray = scene.Camera.ScreenToWorldRay(
            PixelsFromWindowPoint(position), new Vector2(_pixelViewport.X, _pixelViewport.Y));
        
        GameObject? picked = scene.PickAndSelect(ray);

        Logger.Info(picked is null ? "Pick: nothing selected" : $"Pick: selected '{picked.Name}'");
    }
    
    /// <summary>
    /// 按下ESC退出
    /// </summary>
    /// <param name="keyboard"></param>
    /// <param name="key"></param>
    /// <param name="code"></param>
    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
            _window.Close();
    }
    
    /// <summary>
    /// 关闭释放资源
    /// </summary>
    private static void OnClosing()
    {
        _input?.Dispose();
        _renderer?.Dispose(); // GPU resources are released while the context is still alive.
        _scene?.Dispose();
        _graphics?.Dispose();
    }
}