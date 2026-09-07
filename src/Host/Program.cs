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
using System.Buffers.Binary;
using System.IO;
using RobotSimulation.Core.Geometry;

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
    private static string? _cloudPath;

    // rviz 风格鼠标控制参数（若仍觉方向/手感不对，可只改这里的符号或灵敏度）
    private const float RotateSpeed = 0.2f; // 旋转：度/像素
    private const float PanScale = 0.01f; // 平移：目标位移比例
    private const float ZoomDragSpeed = 0.03f; // 右键上下拖拽缩放：距离/像素
    private const float ZoomScrollSpeed = 0.5f; // 滚轮缩放：距离/刻度

    private static Vector2 _lastMousePos;
    private static bool _isDragging = false;

    // ---- 点选拾取（左键单击选中，拖拽仍是旋转视角）----
    private static Vector2 _mouseDownPos;         // 左键按下时的屏幕位置
    private static bool _didDrag;                 // 按下后是否发生明显拖拽（用于区分 点击/拖拽）
    private static GameObject? _selected;         // 当前选中对象（单选：命中即替换，未命中清空）
    private const float ClickDragThreshold = 6f;  // 判定拖拽的位移阈值（像素）

    public static void Main(string[] args)
    {
        // Assimp 原生运行环境准备（Linux 下补 libdl 兼容链接；须早于任何 mesh 导入调用）
        AssimpNative.EnsureRuntime();
        // URDF 定位：命令行参数优先，否则在默认 Assets/Models 下递归查找（逻辑归 Robot 的 UrdfLocator）
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

        // ---- Point cloud demo (PointCloud2-style field layout + per-point colors) ----
        _scene.Add(BuildDemoCloud());

        // Optional: render a point cloud loaded from a file (second CLI argument, .pcd / .ply).
        if (_cloudPath is { } cloudPath)
        {
            if (File.Exists(cloudPath))
                _scene.Add(PointCloud.FromFile(cloudPath));
            else
                Logger.Warning($"点云文件不存在，跳过：{cloudPath}");
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
            _mouseDownPos = new Vector2(mouse.Position.X, mouse.Position.Y);
            _didDrag = false;
            _lastMousePos = _mouseDownPos;
        }
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            // 单击（按下后未拖拽）→ 发射射线拾取选中；拖拽 → 保持旋转视角，不拾取
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

        // 按下点累积位移超过阈值即视为"拖拽"，从而不触发后续"点击拾取"
        if ((position - _mouseDownPos).Length() > ClickDragThreshold)
            _didDrag = true;

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

    /// <summary>
    /// 屏幕像素坐标 → 世界射线 → 拾取并单选高亮。无论命中与否都更新选中状态：
    /// 命中新对象时先取消上一次选中，再把命中对象置高亮；未命中则清空选中。
    /// </summary>
    private static void TryPickAt(Vector2 screenPos)
    {
        var ray = _scene.Camera.ScreenToWorldRay(screenPos, new Vector2(_window.Size.X, _window.Size.Y));
        GameObject? picked = _scene.PickAndHighlight(ray, enable: true);

        // 单选替换：命中对象与上次不同 → 取消上次高亮；未命中 → 清空上次选中
        if (_selected is { } prev && !ReferenceEquals(prev, picked))
            prev.Highlighted = false;
        if (picked is null && _selected is not null)
            _selected.Highlighted = false;
        _selected = picked;

        Logger.Info(picked is null ? "Pick: 未命中" : $"Pick: 选中 '{picked.Name}'");
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
            // Fibonacci sphere：把 n 个点确定性均匀地撒到单位球面上，而非一条螺旋线。
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

            // 各通道先缩放到 0..255 再截断（避免 (uint) 把 [0,1] 提前截成 0/1 导致绝大多数点为纯黑）
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