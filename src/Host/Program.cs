using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Numerics;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
using RobotSimulation.Robot;
using RobotSimulation.Robot.Urdf;

namespace RobotSimulation;

public class Program
{
    private static IWindow _window = null!;

    // 对外只用接口类型；具体后端（GraphicsContext/Renderer）仅在 OnLoad 组装根中创建
    private static IRenderContext _graphics = null!;
    private static SceneGraph _scene = null!;
    private static GameTimer _gameTimer = null!;
    private static RobotModel? _robot;
    private static IRenderer _renderer = null!;
    private static string? _urdfPath;

    // rviz 风格鼠标控制参数（若仍觉方向/手感不对，可只改这里的符号或灵敏度）
    private const float RotateSpeed = 0.2f; // 旋转：度/像素
    private const float PanScale = 0.01f; // 平移：目标位移比例
    private const float ZoomDragSpeed = 0.03f; // 右键上下拖拽缩放：距离/像素
    private const float ZoomScrollSpeed = 0.5f; // 滚轮缩放：距离/刻度

    private static Vector2 _lastMousePos;
    private static bool _isDragging = false;

    public static void Main(string[] args)
    {
        // Assimp 原生运行环境准备（Linux 下补 libdl 兼容链接；须早于任何 mesh 导入调用）
        AssimpNative.EnsureRuntime();
        // URDF 定位：命令行参数优先，否则在默认 Assets/Models 下递归查找（逻辑归 Robot 的 UrdfLocator）
        _urdfPath = UrdfLocator.Find(args is { Length: > 0 } ? args[0] : null);

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
        // 渲染上下文：GL 实例仅在宿主组装根创建并注入，之后只经 IRenderContext 使用
        GL gl = _window.CreateOpenGL(); // Host 唯一允许接触 GL 的位置
        var graphics = new GraphicsContext(gl); // 具体后端类型只在组装根出现
        _graphics = graphics;
        _scene = new SceneGraph();

        _graphics.Resized += (w, h) => _scene.Camera.AspectRatio = w / (float)h;
        _graphics.Resize(_window.Size.X, _window.Size.Y);

        // 默认场景环境（网格地面 / 默认灯光 / 世界坐标轴 / 相机姿态）由 SceneGraph 构造时自动装配，
        // 这里无需手动调用。

        // 渲染器：GPU 网格/材质只存在于它内部（Scene/GameObject 均为纯数据）；
        // 标准通道着色器（模型/线条/点云/天空）内嵌于 OpenGL 后端，宿主无需指定路径
        _renderer = new Renderer(graphics);

        // ---- URDF 机器人：加载命令行指定或 Assets/Models 下的第一个 .urdf 并显示 ----
        if (_urdfPath is { } modelPath)
        {
            Logger.Info($"加载模型：{modelPath}");
            _robot = RobotModel.ParseFile(modelPath); // 路径解析走 Robot 默认 FileSystemAssetResolver（找不到会直接报错）
            Logger.Info(
                $"'{_robot.RobotName}': links={_robot.Description.Links.Count}, joints={_robot.Description.Joints.Count}");
            _scene.Add(_robot); // 与其它 GameObject 平等地加入场景，由渲染器统一绘制
        }
        else
        {
            Logger.Warning("未指定 URDF。用法：RobotSimulation.Host <model.urdf>，或把 .urdf 放入 Assets/Models/ 后运行。");
        }

        // ---- 最小示例内容：往场景里加几个可见对象（默认网格/灯光/世界坐标轴已由 SceneGraph 构造自动装配）----
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
        // 模型静止显示（不添加自动动画）；需要驱动时由外部调用 robot.SetJointValue(...)

        _graphics.Clear(_scene.BackgroundColor); // 背景色来自场景默认显示设置（中等灰）
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
        _renderer?.Dispose(); // GPU 资源统一在此释放
        _scene?.Dispose();
        _graphics?.Dispose();
    }

    // ---- 输入事件（rviz 风格）----
    //   左键拖拽 = 旋转视角    中键拖拽 = 平移    右键拖拽 = 缩放（上放大/下缩小）    滚轮 = 缩放
    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape)
            _window.Close();
        else if (key == Key.W)
            _scene.Camera.Zoom(0.6f); // W = 拉近（备用缩放，避免依赖滚轮）
        else if (key == Key.S)
            _scene.Camera.Zoom(-0.6f); // S = 拉远
    }

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left || button == MouseButton.Middle || button == MouseButton.Right)
        {
            _isDragging = true;
            _lastMousePos = new Vector2(mouse.Position.X, mouse.Position.Y);
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left || button == MouseButton.Middle || button == MouseButton.Right)
            _isDragging = false;
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_isDragging) return;
        Vector2 delta = new Vector2(position.X, position.Y) - _lastMousePos;
        _lastMousePos = new Vector2(position.X, position.Y);

        if (mouse.IsButtonPressed(MouseButton.Left))
            // 水平取反：让场景"跟随"鼠标拖动方向，而不是反向滚动
            _scene.Camera.Rotate(-delta.X * RotateSpeed, -delta.Y * RotateSpeed);
        else if (mouse.IsButtonPressed(MouseButton.Middle))
            _scene.Camera.Pan(delta * PanScale);
        else if (mouse.IsButtonPressed(MouseButton.Right))
            // 上拖 dy<0 → Zoom 为正 → 距离减小 → 放大
            _scene.Camera.Zoom(-delta.Y * ZoomDragSpeed);
    }

    private static void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        float dy = scroll.Y != 0f ? scroll.Y : scroll.X; // 兼容部分平台把垂直滚动放 X
        if (dy == 0f)
            return;

        _scene.Camera.Zoom(dy * ZoomScrollSpeed);
    }
}