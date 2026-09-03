namespace RobotSimulation.Core.GameObjects;

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

    /// <summary>材质描述（CPU）。为 null 表示该节点不绘制（或使用默认外观）。</summary>
    public MaterialData? MaterialData { get; set; }

    public bool Visible { get; set; } = true;

    public GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")
    {
        Transform = new Transform(this);
        MeshData = meshData;
        MaterialData = materialData;
        if (!string.IsNullOrEmpty(name))
            Name = name;
    }

    /// <summary>
    /// 通过外部模型文件加载模型数据与材质
    /// </summary>
    /// <param name="filePath"></param>
    public void LoadModel(string filePath)
    {
        
    }

    /// <summary>
    /// 单帧更新逻辑
    /// </summary>
    /// <param name="deltaTime"></param>
    public virtual void Update(double deltaTime) { }
}