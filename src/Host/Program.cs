using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Numerics;
using RobotSimulation.Core.GameObjects;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
using RobotSimulation.Robot;
using RobotSimulation.Robot.Description;

namespace RobotSimulation;

public class Program
{
    private static IWindow _window;
    // 对外只用接口类型；具体后端（GraphicsContext/Renderer）仅在 OnLoad 组装根中创建
    private static IRenderContext _graphics;
    private static SceneGraph _scene;
    private static GameTimer _gameTimer;
    private static RobotGameObject _robot;
    private static IRenderer _renderer;
    private static float _demoTime;
    // rviz 风格鼠标控制参数（若仍觉方向/手感不对，可只改这里的符号或灵敏度）
    private const float RotateSpeed = 0.2f;      // 旋转：度/像素
    private const float PanScale = 0.01f;        // 平移：目标位移比例
    private const float ZoomDragSpeed = 0.03f;   // 右键上下拖拽缩放：距离/像素
    private const float ZoomScrollSpeed = 0.5f;  // 滚轮缩放：距离/刻度

    /// <summary>演示用 URDF：转台式机械臂（revolute 转台 + prismatic 滑块）。</summary>
    private const string DemoUrdf = """
        <robot name="demo_arm">
          <link name="base_link">
            <visual>
              <origin xyz="0 0 0.1"/>
              <geometry><box size="0.32 0.32 0.2"/></geometry>
              <material name="base_mat"/>
            </visual>
          </link>
          <joint name="j_yaw" type="revolute">
            <parent link="base_link"/>
            <child link="turntable"/>
            <origin xyz="0 0 0.2"/>
            <axis xyz="0 0 1"/>
          </joint>
          <link name="turntable">
            <visual>
              <origin xyz="0 0 0.03"/>
              <geometry><cylinder radius="0.14" length="0.06"/></geometry>
            </visual>
            <visual>
              <origin xyz="0.28 0 0.03" rpy="0 1.5707963 0"/>
              <geometry><cylinder radius="0.02" length="0.56"/></geometry>
              <material name="arm_mat"/>
            </visual>
          </link>
          <joint name="j_lift" type="prismatic">
            <parent link="turntable"/>
            <child link="slider"/>
            <origin xyz="0.28 0 0.06"/>
            <axis xyz="0 0 1"/>
          </joint>
          <link name="slider">
            <visual>
              <origin xyz="0 0 0.1"/>
              <geometry><box size="0.05 0.05 0.2"/></geometry>
              <material name="slider_mat"/>
            </visual>
          </link>
          <material name="base_mat"><color rgba="0.85 0.35 0.15 1"/></material>
          <material name="arm_mat"><color rgba="0.3 0.55 0.9 1"/></material>
          <material name="slider_mat"><color rgba="0.95 0.85 0.1 1"/></material>
        </robot>
        """;

    private static Vector2 _lastMousePos;
    private static bool _isDragging = false;

    public static void Main(string[] args)
    {
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
            Console.WriteLine($"Unhandled exception: {ex}");
            Console.ReadLine();
        }
    }

    private static void OnLoad()
    {
        // 渲染上下文：GL 实例仅在宿主组装根创建并注入，之后只经 IRenderContext 使用
        GL gl = _window.CreateOpenGL();                // Host 唯一允许接触 GL 的位置
        var graphics = new GraphicsContext(gl);        // 具体后端类型只在组装根出现
        _graphics = graphics;
        _scene = new SceneGraph();

        _graphics.Resized += (w, h) => _scene.Camera.AspectRatio = w / (float)h;
        _graphics.Resize(_window.Size.X, _window.Size.Y);

        _scene.LightPosition = new Vector3(4, 5, 10);   // Z-up：光源置于地面上方
        _scene.LightColor = new Vector3(1, 1, 1);

        // 渲染器：GPU 网格/材质只存在于它内部（Scene/GameObject 均为纯数据）
        _renderer = new Renderer(
            graphics,
            "Assets/Shaders/Model/Standard.vert",
            "Assets/Shaders/Model/Standard.frag");

        // ---- 地面（世界 Z-up；图元也是纯数据）----
        AddPrimitive(_scene, PrimitiveBuilder.CreatePlane(6f, 6f),
            new Vector4(0.62f, 0.62f, 0.68f, 1f), "Ground", Vector3.Zero);

        // ---- URDF 机器人：解析 → 创建 GameObject（纯数据，无任何渲染参数）----
        RobotModel robot = RobotModel.Parse(DemoUrdf);
        Console.WriteLine($"[URDF] '{robot.Name}': links={robot.Description.Links.Count}, joints={robot.Description.Joints.Count}");
        _robot = robot.Instantiate();
        _scene.Add(_robot);   // 与其它 GameObject 平等地加入场景，由渲染器统一绘制

        // 相机看向机器人
        _scene.Camera.Target = new Vector3(0f, 0f, 0.35f);
        _scene.Camera.Distance = 4.5f;
        _scene.Camera.Pitch = 20f;

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
        // 演示：每帧驱动 URDF 机器人关节（转台往复旋转 + 末端滑块升降）
        _demoTime += (float)deltaTime;
        if (_robot != null)
        {
            _robot.SetJointValue("j_yaw", MathF.Sin(_demoTime * 1.1f) * 0.9f);
            _robot.SetJointValue("j_lift", (MathF.Sin(_demoTime * 2.4f) + 1f) * 0.06f);
        }

        _graphics.Clear(new Vector4(0.392f, 0.584f, 0.929f, 1f)); // CornflowerBlue
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
        _renderer?.Dispose();   // GPU 资源统一在此释放
        _scene?.Dispose();
        _graphics?.Dispose();
    }

    // ---- 输入事件（rviz 风格）----
    //   左键拖拽 = 旋转视角    中键拖拽 = 平移    右键拖拽 = 缩放（上放大/下缩小）    滚轮 = 缩放
    private static void OnKeyDown(IKeyboard keyboard, Key key, int code)
    {
        if (key == Key.Escape) _window.Close();
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
        _scene.Camera.Zoom(scroll.Y * ZoomScrollSpeed);
    }

    // ---- 场景对象摆放（数据化：MeshData + MaterialData，无 GPU 上传）----

    /// <summary>
    /// 用图元数据创建带材质描述的 GameObject 并加入场景；GPU 上传由 Renderer 在绘制时完成。
    /// </summary>
    private static void AddPrimitive(SceneGraph scene, MeshData data, Vector4 color, string name, Vector3 position)
    {
        var material = new MaterialData { BaseColor = color };
        var go = new GameObject(data, material, name);
        go.Transform.Position = position;
        scene.Add(go);
    }
}