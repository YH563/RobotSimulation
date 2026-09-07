using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 场景节点。只持有场景数据：Transform、模型数据（CPU）与材质描述（CPU），
/// 不管理任何 GPU 资源——网格/材质的 GPU 实例化由渲染器在渲染时完成。
/// 因此可以在任意线程创建、跨场景复用，加入 SceneGraph 后即被渲染器遍历绘制。
/// </summary>
public class GameObject
{
    /// <summary>
    /// 对象名称，用于调试与按名查找（例如 URDF link/joint 名）。
    /// </summary>
    public string Name { get; set; } = "";

    public Transform Transform { get; }

    /// <summary>3D 模型数据（CPU 顶点/索引）。为 null 表示该节点无几何（骨架/纯层级节点）。</summary>
    public MeshData? MeshData { get; set; }

    /// <summary>
    /// 线条数据（CPU 线段集，配合 <see cref="MaterialData.PassKind"/> = Line 使用）。
    /// 用于网格地面、坐标轴等无线光线条；与 <see cref="MeshData"/> 互斥。
    /// </summary>
    public LineData? LineData { get; set; }

    /// <summary>
    /// Point data (CPU, used with <see cref="MaterialData.PassKind"/> = Point, e.g. point clouds).
    /// </summary>
    public PointCloud2Data? PointData { get; set; }

    /// <summary>Rendering size (in pixels) for GL_POINTS; used by the Point pass.</summary>
    public float PointSize { get; set; } = 3f;

    /// <summary>
    /// 本节点的局部坐标系（子对象方式）：开启时在其 Transform 下挂一个 RGB 小坐标轴，
    /// 随本对象一起旋转/平移/缩放，渲染走普通递归，无需渲染器特判。
    /// </summary>
    public bool ShowLocalAxes
    {
        get => _showLocalAxes;
        set
        {
            if (_showLocalAxes == value)
                return;
            _showLocalAxes = value;
            if (value)
            {
                _localAxes ??= new Axes(LocalAxesLength, name: "local-axes");
                _localAxes.SetSubtreePickable(false);   // 局部坐标轴是显示辅助，不应参与拾取
                _localAxes.Transform.Parent = Transform;
            }
            else
            {
                if (_localAxes is not null)
                    _localAxes.Transform.Parent = null;   // 从树上摘掉（保留对象以便再挂）
            }
        }
    }

    /// <summary>局部坐标轴的默认长度（挂载时按此创建；此后改它需要重新挂载）。</summary>
    public float LocalAxesLength { get; set; } = 0.3f;

    /// <summary>已挂载的局部坐标轴（<see cref="ShowLocalAxes"/> 开启后非空；可按需再调）。</summary>
    public Axes? LocalAxes => _localAxes;

    private bool _showLocalAxes;
    private Axes? _localAxes;

    /// <summary>材质描述（CPU）。为 null 表示该节点不绘制（或使用默认外观）。</summary>
    public MaterialData? MaterialData { get; set; }

    public bool Visible { get; set; } = true;

    /// <summary>
    /// 是否高亮（默认 false）。渲染器对高亮节点做一件事：最终颜色向 <see cref="HighlightColor"/>
    /// 混合（tint），用于点选后的选中视觉反馈。默认关闭；机器人（<c>RobotModel</c>）默认不高亮，
    /// 由宿主在鼠标命中时经 <see cref="SceneGraph.PickAndHighlight"/> 置为 true。
    /// </summary>
    public bool Highlighted { get; set; }

    /// <summary>高亮颜色（<see cref="Highlighted"/> 为 true 时由渲染后端读取，仅 CPU 数据）。</summary>
    public Vector4 HighlightColor { get; set; } = new(1f, 0.72f, 0.16f, 1f);

    /// <summary>
    /// 是否参与射线拾取（<see cref="SceneGraph.Pick"/>）。默认 true。
    /// 默认装配在场景里的显示辅助（世界/局部坐标轴、网格地面等）会设为 false，
    /// 避免他们挡住/抢先目标对象的拾取；用户也可对任意子树整体屏蔽。
    /// </summary>
    public bool Pickable { get; set; } = true;

    /// <summary>
    /// 递归设置本节点及所有后代的 <see cref="Pickable"/>（用于一次性屏蔽某根子树，如坐标轴）。
    /// </summary>
    public void SetSubtreePickable(bool value)
    {
        Pickable = value;
        foreach (var child in Transform.Children)
            child.Owner.SetSubtreePickable(value);
    }

    public GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")
    {
        Transform = new Transform(this);
        MeshData = meshData;
        MaterialData = materialData;
        if (!string.IsNullOrEmpty(name))
            Name = name;
    }

    /// <summary>
    /// 从外部模型文件（STL/OBJ/DAE/glTF 等，经 <see cref="AssimpModelLoader"/>）加载几何与材质，
    /// 填充本对象的 <see cref="MeshData"/> / <see cref="MaterialData"/>（纯 CPU 数据，任意线程可调）。
    /// 仅适用于“文件恰好含一个 mesh”的模型；多 submesh 文件请改用
    /// <see cref="AssimpModelLoader.Load"/> 获取完整结果后自行分组挂载。
    /// </summary>
    /// <param name="filePath">模型文件路径。</param>
    /// <param name="options">导入选项（翻转、缩放等）；null 使用默认。</param>
    public void LoadModel(string filePath, LoadOptions? options = null)
    {
        LoadedModel model = AssimpModelLoader.Load(filePath, options);
        if (model.Meshes.Count != 1)
            throw new InvalidOperationException(
                $"{model.FilePath} 含 {model.Meshes.Count} 个 submesh：GameObject.LoadModel 仅支持单 mesh 模型，请改用 AssimpModelLoader.Load。");

        LoadedMesh mesh = model.Meshes[0];
        MeshData = mesh.MeshData;
        MaterialData = mesh.MaterialData;
    }

    /// <summary>
    /// 单帧更新逻辑
    /// </summary>
    /// <param name="deltaTime"></param>
    public virtual void Update(double deltaTime) { }
}