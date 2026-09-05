using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 场景图容器：持有场景根对象树、活动相机、一组光源，以及场景的显示默认设置
/// （背景/环境光/网格地面）。设置直接作为本类可读写属性 + 默认装配方法存在，
/// 不单独拆"设置/默认值"类——开箱即用的数值就是下面的默认值。
/// </summary>
public class SceneGraph : IDisposable
{
    private readonly List<GameObject> _roots = new();
    private readonly List<Light> _lights = new();

    /// <summary>场景根节点（供渲染器遍历与外部只读访问）。</summary>
    public IReadOnlyList<GameObject> Roots => _roots;

    /// <summary>场景活动相机（默认内置一个轨道相机，可整体替换）。</summary>
    public Camera Camera { get; set; } = new Camera();

    // ---- 场景显示默认设置（宿主/UI 直接读写这些属性即可）----

    /// <summary>清屏/背景色（中等灰，作为默认"天空"底色）。</summary>
    public Vector4 BackgroundColor { get; set; } = new(0.20f, 0.22f, 0.25f, 1f);

    /// <summary>环境光（RGB 强度，0-1）。阴面亮度由它兜底，避免过黑。</summary>
    public Vector3 AmbientColor { get; set; } = new(0.30f, 0.32f, 0.36f);

    /// <summary>是否显示默认网格地面（<see cref="AddDefaultGrid"/> 依据它创建）。</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>是否显示默认世界原点坐标轴（<see cref="AddDefaultWorldAxes"/> 依据它创建）。</summary>
    public bool ShowWorldAxes { get; set; } = true;

    /// <summary>默认世界原点坐标轴的长度。</summary>
    public float WorldAxesLength { get; set; } = 0.5f;

    /// <summary>单格边长（米）。</summary>
    public float GridCellSize { get; set; } = 1f;

    /// <summary>中心往每个方向的格数（总跨度 = 2 × GridCellCount × GridCellSize）。</summary>
    public int GridCellCount { get; set; } = 10;

    /// <summary>网格线颜色。</summary>
    public Vector4 GridColor { get; set; } = new(0.30f, 0.30f, 0.35f, 1f);

    /// <summary>场景中的所有光源（由 <see cref="Add"/> 自动收集）。</summary>
    public IReadOnlyList<Light> Lights => _lights;

    private bool _disposed;

    /// <summary>
    /// 构造即完成默认场景装配：默认相机姿态、默认灯光、网格地面、世界原点坐标轴。
    /// 不需要宿主再手动调用（宿主只负责添加上层内容，如 URDF 机器人、演示对象）。
    /// </summary>
    public SceneGraph()
    {
        ApplyDefaultCamera();
        AddDefaultLights();
        AddDefaultGrid();
        AddDefaultWorldAxes();
    }

    /// <summary>
    /// 添加对象（自动识别是否为根节点）。若 <paramref name="obj"/> 是 <see cref="Light"/>
    /// 且此前未登记，则同时加入 <see cref="Lights"/>。
    /// </summary>
    public void Add(GameObject? obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (obj.Transform.Parent == null)
            _roots.Add(obj);

        if (obj is Light light && !_lights.Contains(light))
            _lights.Add(light);
    }

    /// <summary>
    /// 移除游戏对象，自动从父级或根列表中移除；若是 <see cref="Light"/> 同时从光源列表移除。
    /// </summary>
    public void Remove(GameObject? obj)
    {
        if (obj == null) return;

        if (obj is Light light)
            _lights.Remove(light);

        if (obj.Transform.Parent == null)
        {
            // 如果是根节点，直接从根列表移除
            _roots.Remove(obj);
        }
        else
        {
            // 如果不是根节点，将 Parent 设为 null，自动从父级 Children 移除
            obj.Transform.Parent = null;
        }
    }

    // ------------------------------------------------------------------
    // 默认场景装配（网格 / 灯光 / 相机初始姿态；无需独立"设置类"）
    // ------------------------------------------------------------------

    /// <summary>按当前网格属性创建并加入网格地面；<see cref="ShowGrid"/> 关闭时不创建。</summary>
    public Grid? AddDefaultGrid()
    {
        if (!ShowGrid)
            return null;

        var grid = new Grid(GridCellSize, GridCellCount, GridColor);
        Add(grid);
        return grid;
    }

    /// <summary>加入默认主光 + 补光。</summary>
    public void AddDefaultLights()
    {
        var key = new Light("key_light") { Color = Vector3.One, Intensity = 0.85f };
        key.Transform.Position = new Vector3(4f, 5f, 10f);   // Z-up：置于地面上方
        Add(key);

        var fill = new Light("fill_light") { Color = new Vector3(0.55f, 0.55f, 0.65f), Intensity = 0.4f };
        fill.Transform.Position = new Vector3(-5f, -3f, 6f);
        Add(fill);
    }

    /// <summary>按 <see cref="ShowWorldAxes"/> 创建并加入世界原点的默认全局坐标轴；关闭时不创建。</summary>
    public Axes? AddDefaultWorldAxes()
    {
        if (!ShowWorldAxes)
            return null;

        var axes = new Axes(WorldAxesLength, name: "world-axes") { Transform = { Position = Vector3.Zero } };
        Add(axes);
        return axes;
    }

    /// <summary>把相机摆到默认初始姿态（就近观察场景）。</summary>
    public void ApplyDefaultCamera()
    {
        Camera.Target = new Vector3(0f, 0f, 0.3f);
        Camera.Distance = 3.2f;
        Camera.Pitch = 25f;
        Camera.Yaw = -90f;
    }

    /// <summary>
    /// 递归更新所有节点（由后台线程调用，严禁操作 OpenGL）
    /// </summary>
    public void Update(double deltaTime)
    {
        foreach (var root in _roots)
            UpdateRecursive(root, deltaTime);
    }

    private void UpdateRecursive(GameObject node, double deltaTime)
    {
        node.Update(deltaTime);
        foreach (var child in node.Transform.Children)
            UpdateRecursive(child.Owner, deltaTime);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // GPU 网格/材质由渲染器（Renderer）统一释放；
        // 场景本身只持有数据引用，此处仅清理节点列表。
        _roots.Clear();
        _lights.Clear();
        _disposed = true;
    }
}