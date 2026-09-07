using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.Robot.Description;
using RobotSimulation.Robot.State;
using RobotSimulation.Robot.Urdf;

namespace RobotSimulation.Robot;

/// <summary>
/// 机器人 —— 一棵完整的 <see cref="GameObject"/> 树。
/// 本类型是这棵树的根（根 link 的骨架节点即它本身），同时是 URDF/描述的单一入口：
/// 读取 URDF 或从任意 <see cref="RobotDescription"/> 构造，立即得到一个可加入场景、
/// 可驱动关节、可继续扩展的机器人 GameObject 树。节点只持 CPU 数据（MeshData/MaterialData），
/// 无任何渲染参数——由调用方 scene.Add 后即被渲染器遍历绘制。
///
/// 使用示例：
/// <code>
/// var robot = RobotModel.ParseFile("arm.urdf");    // 读 URDF → 完整机器人树
/// scene.Add(robot);                                 // 与其它 GameObject 平等加入场景
/// robot.SetJointValue("shoulder", 1.2f);           // 驱动关节（旋转弧度 / 平移米）
///
/// // 扩展点：任何能产出 RobotDescription 的解析器（SDF/自定义）都能构建同样的机器人树
/// var another = new RobotModel(someDescription);
/// </code>
/// </summary>
public sealed class RobotModel : GameObject
{
    private readonly RobotDescription _description;
    private readonly IAssetResolver? _resolver;

    // 可驱动关节绑定（按 RobotDescription.Joints 中出现顺序）
    private readonly List<Joint> _drivableJoints = new();
    private readonly Dictionary<string, int> _drivableIndexByName = new(StringComparer.Ordinal);
    private readonly List<GameObject> _drivableChildLinks = new();
    private float[] _jointValues = Array.Empty<float>();

    // ------------------------------------------------------------------
    // 构造（扩展点：任意描述源 → 机器人 GameObject 树）
    // ------------------------------------------------------------------

    /// <summary>
    /// 从静态描述构建完整机器人树：每个 link 一个骨架 GameObject（根 link = 本对象自身），
    /// visual 为携带模型数据/材质描述的子节点，关节按 joint.origin 把子树挂到父 link 下。
    /// 要求描述为单根。网格与材质的 GPU 实例化由渲染器在绘制时完成。
    /// </summary>
    /// <param name="description">机器人静态描述（URDF/SDF/自定义解析产物）。</param>
    /// <param name="resolver">资源路径解析器（mesh/texture）；null 时使用文件系统默认实现。</param>
    public RobotModel(RobotDescription description, IAssetResolver? resolver = null)
        : base(null, null, RequireSingleRoot(description))
    {
        _description = description;
        _resolver = resolver;
        RobotName = description.Name ?? string.Empty;
        BuildTree();
        LockTree(); // 构建完成后锁定整棵子树：子 link 位姿只能经 RobotModel 内置接口修改
    }

    /// <summary>校验描述非空且恰有一个根 link，返回根 link 名（将作为本 GameObject 的 Name）。</summary>
    private static string RequireSingleRoot(RobotDescription? description)
    {
        if (description is null)
            throw new ArgumentNullException(nameof(description));
        if (description.RootLinks.Count != 1)
            throw new ArgumentException(
                $"机器人 '{description.Name}' 的根 link 数量为 {description.RootLinks.Count}，" +
                "作为单个 GameObject 构建需要恰好 1 个根。",
                nameof(description));
        return description.RootLinks[0].Name;
    }

    // ------------------------------------------------------------------
    // URDF 便捷工厂：读 URDF → 完整机器人树
    // ------------------------------------------------------------------

    /// <summary>解析内存中的 URDF 文本并构建完整机器人树。</summary>
    /// <param name="urdfXml">URDF 源文本。</param>
    /// <param name="baseDirectory">相对资源路径的基准目录；可为 null。</param>
    /// <param name="resolver">资源路径解析器（mesh/texture）；可为 null。</param>
    /// <exception cref="UrdfParseException">XML 或 URDF 语义非法时抛出。</exception>
    public static RobotModel Parse(string urdfXml, string? baseDirectory = null, IAssetResolver? resolver = null)
    {
        if (urdfXml is null)
            throw new UrdfParseException("URDF 文本为 null。");

        XDocument document;
        try
        {
            document = XDocument.Parse(urdfXml);
        }
        catch (XmlException ex)
        {
            throw new UrdfParseException($"URDF XML 格式错误：{ex.Message}", ex);
        }

        RobotDescription description = new UrdfParser().Parse(document, baseDirectory);
        return new RobotModel(description, resolver);
    }

    /// <summary>读取 URDF 文件并构建完整机器人树；文件目录自动作为相对资源路径的基准。</summary>
    /// <exception cref="UrdfParseException">解析失败时抛出。</exception>
    /// <exception cref="FileNotFoundException">文件不存在时抛出。</exception>
    public static RobotModel ParseFile(string path, IAssetResolver? resolver = null)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"URDF 文件不存在：{fullPath}", fullPath);

        string xml = File.ReadAllText(fullPath);
        return Parse(xml, Path.GetDirectoryName(fullPath), resolver);
    }

    // ------------------------------------------------------------------
    // 描述与 headless（FK / 序列化）
    // ------------------------------------------------------------------

    /// <summary>构建本树所用的纯数据描述。</summary>
    public RobotDescription Description => _description;

    /// <summary>
    /// 机器人名称（URDF &lt;robot name&gt;）。注意与 <see cref="GameObject.Name"/>
    /// （根 link 名）区分——GameObject.Name 指场景骨架节点的名字。
    /// </summary>
    public string RobotName { get; }

    /// <summary>资源路径解析器（mesh/texture 导入时使用）。</summary>
    public IAssetResolver? AssetResolver => _resolver;

    /// <summary>创建 headless 纯状态对象（不依赖场景/渲染）：关节值 + FK。</summary>
    public RobotState CreateState() => new RobotState(_description);

    /// <summary>
    /// 机器人整体（根 link）在其父坐标系下的位姿。
    /// 读、写都走本类型内置接口：写操作经 <see cref="Transform.SetLocalPose"/> 绕过只读保护，
    /// 与 <c>SetJointValue</c> 一样是唯一允许改动整棵树位姿的入口（根 link 无 incoming 关节，可自由摆放整机）。
    /// </summary>
    public Matrix4x4 RootPose
    {
        get => Transform.GetLocalMatrix();
        set
        {
            if (Matrix4x4.Decompose(value, out Vector3 scale, out Quaternion rotation, out Vector3 position))
                Transform.SetLocalPose(position, rotation, scale);
            else
                Transform.SetLocalPose(value.Translation, Quaternion.Identity, Vector3.One);
        }
    }

    // ------------------------------------------------------------------
    // 树构建
    // ------------------------------------------------------------------

    private void BuildTree()
    {
        string rootLinkName = _description.RootLinks[0].Name;

        // 1) 每个 link 一个骨架节点（根 = 本对象自身）
        var linkNodes = new Dictionary<string, GameObject>(StringComparer.Ordinal) { [rootLinkName] = this };
        foreach (Link link in _description.Links)
        {
            if (link.Name == rootLinkName)
                continue;
            linkNodes[link.Name] = new GameObject(null, null, link.Name);
        }

        // 2) 挂载 visual 子节点（携带 CPU 模型数据与材质描述）
        foreach (Link link in _description.Links)
        {
            GameObject linkNode = linkNodes[link.Name];
            for (int i = 0; i < link.VisualElements.Count; i++)
            {
                foreach (GameObject visualNode in BuildVisualNodes(link.Name, i, link.VisualElements[i]))
                    visualNode.Transform.Parent = linkNode.Transform;
            }
        }

        // 3) 关节拓扑：子 link 骨架挂到父 link 下，施加 joint.origin（零位）
        foreach (Joint joint in _description.Joints)
        {
            GameObject parent = linkNodes[joint.ParentLinkName];
            GameObject child = linkNodes[joint.ChildLinkName];
            child.Transform.Parent = parent.Transform;
            ApplyPose(child.Transform, joint.Origin);
        }

        // 4) 注册可驱动关节绑定
        ConfigureDriving(linkNodes);
    }

    /// <summary>收集可驱动关节（Revolute/Continuous/Prismatic，按描述顺序）及其驱动目标子 link。</summary>
    private void ConfigureDriving(IReadOnlyDictionary<string, GameObject> linkNodes)
    {
        foreach (Joint joint in _description.Joints)
        {
            if (joint.Type is not (JointType.Revolute or JointType.Continuous or JointType.Prismatic))
                continue;

            _drivableIndexByName[joint.Name] = _drivableJoints.Count;
            _drivableJoints.Add(joint);
            _drivableChildLinks.Add(linkNodes[joint.ChildLinkName]);
        }

        _jointValues = new float[_drivableJoints.Count];
    }

    // ------------------------------------------------------------------
    // 关节驱动（可视化）
    // ------------------------------------------------------------------

    /// <summary>可驱动关节列表（Revolute/Continuous/Prismatic，按描述顺序）。</summary>
    public IReadOnlyList<Joint> DrivableJoints => _drivableJoints;

    /// <summary>可驱动关节数量（即 <see cref="ApplyJointValues"/> 所需数组长度）。</summary>
    public int DrivableJointCount => _drivableJoints.Count;

    /// <summary>
    /// 按名称设置关节值（旋转弧度 / 平移沿轴的米），并立即更新对应子 link 的局部 Transform。
    /// 仅操作 Transform（纯数据），不触碰 OpenGL。
    /// </summary>
    public void SetJointValue(string name, float value)
    {
        int index = ResolveDrivable(name);
        _jointValues[index] = value;
        ApplyJointNode(index);
    }

    /// <summary>按可驱动关节顺序批量设置关节值并一次更新整树。</summary>
    public void ApplyJointValues(IReadOnlyList<float> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));
        if (values.Count != _drivableJoints.Count)
            throw new ArgumentException(
                $"关节值数量({values.Count})与可驱动关节数({_drivableJoints.Count})不一致。",
                nameof(values));

        for (int i = 0; i < values.Count; i++)
        {
            _jointValues[i] = values[i];
            ApplyJointNode(i);
        }
    }

    private int ResolveDrivable(string name)
    {
        if (!_drivableIndexByName.TryGetValue(name, out int index))
            throw new KeyNotFoundException(
                $"关节 '{name}' 不存在或不可驱动（Fixed 关节不占驱动位）。");
        return index;
    }

    private void ApplyJointNode(int index)
    {
        Joint joint = _drivableJoints[index];
        GameObject childLink = _drivableChildLinks[index];

        // 子 link 局部位姿 = joint.origin * 关节运动(q)；行主序与 GetModelMatrix 级联一致
        Matrix4x4 local = joint.Origin * JointMotion(joint, _jointValues[index]);
        ApplyPose(childLink.Transform, local);
    }

    private static Matrix4x4 JointMotion(Joint joint, float q)
    {
        switch (joint.Type)
        {
            case JointType.Revolute:
            case JointType.Continuous:
                return Matrix4x4.CreateFromQuaternion(
                    Quaternion.CreateFromAxisAngle(joint.Axis, q));
            case JointType.Prismatic:
                return Matrix4x4.CreateTranslation(joint.Axis * q);
            default:
                return Matrix4x4.Identity;
        }
    }

    // ------------------------------------------------------------------
    // visual / 材质构建
    // ------------------------------------------------------------------

    /// <summary>
    /// 为 link 的一个 visual 产出可绘制子节点。
    /// 图元：单个基础图元节点；Mesh：解析文件后按 submesh 逐一产出节点（每个带各自材质）。
    /// 资源解析失败/文件导入失败会直接抛出异常并记录日志（不做静默跳过），便于用户修正 URDF 路径。
    /// </summary>
    private IReadOnlyList<GameObject> BuildVisualNodes(string linkName, int index, VisualElement visual)
    {
        if (visual.Geometry is MeshGeometry mesh)
            return LoadMeshVisual(linkName, index, visual, mesh);

        // 图元几何 → 对应的基础图元 GameObject（构造即生成 CPU MeshData）
        GameObject node = BuildPrimitiveNode(visual.Geometry);
        node.Name = $"{linkName}:visual{index}";
        node.MaterialData = BuildMaterialData(visual.Material);
        ApplyPose(node.Transform, visual.LocalTransform);
        return new[] { node };
    }

    /// <summary>URDF mesh 几何 → 文件导入 → 子节点（每个 submesh 一个，URDF 材质覆盖文件材质）。</summary>
    private IReadOnlyList<GameObject> LoadMeshVisual(string linkName, int index, VisualElement visual,
        MeshGeometry mesh)
    {
        IAssetResolver resolver = _resolver ?? new FileSystemAssetResolver();
        string path = resolver.Resolve(mesh.Uri, _description.SourceBaseDirectory)
                      ?? throw new FileNotFoundException(
                          $"无法解析 mesh 资源 '{mesh.Uri}'（link '{linkName}' 的 visual#{index}）。请检查 URDF 路径。",
                          mesh.Uri);

        LoadedModel model;
        try
        {
            model = AssimpModelLoader.Load(path);
        }
        catch (Exception ex)
        {
            Logger.Error($"[RobotModel] mesh 导入失败：{path}（link '{linkName}' 的 visual#{index}）。", ex);
            throw new IOException(
                $"mesh 导入失败：{path}（link '{linkName}' 的 visual#{index}）。请检查文件是否损坏或格式不受支持。", ex);
        }

        string baseName = $"{linkName}:visual{index}";
        var nodes = new List<GameObject>(model.Meshes.Count);
        for (int m = 0; m < model.Meshes.Count; m++)
        {
            LoadedMesh sub = model.Meshes[m];
            GameObject node = new(
                sub.MeshData,
                MergeMeshMaterial(sub.MaterialData, visual.Material),
                model.Meshes.Count > 1 ? $"{baseName}.{m}" : baseName);
            ApplyPose(node.Transform, visual.LocalTransform);
            nodes.Add(node);
        }

        Logger.Debug($"[RobotModel] '{baseName}' ← {path}（{model.Meshes.Count} 个 submesh）");
        return nodes;
    }

    /// <summary>合并规则：文件自带材质是基线；URDF material 显式给出的 color/texture 覆盖。</summary>
    private MaterialData MergeMeshMaterial(MaterialData fileMaterial, MaterialElement? urdf)
    {
        // 每次导入产生的材质都是新实例，可安全就地覆盖（文件材质为基线）
        if (urdf?.Color is { } color)
            fileMaterial.BaseColor = color;
        if (urdf?.TextureFile is { } textureFile)
        {
            IAssetResolver resolver = _resolver ?? new FileSystemAssetResolver();
            if (resolver.Resolve(textureFile, _description.SourceBaseDirectory) is { } texturePath)
                fileMaterial.AlbedoTexture = TextureReference.FromFile(texturePath);
        }

        return fileMaterial;
    }

    /// <summary>把 URDF 图元几何实例化为对应的基础图元 GameObject（Box/Sphere/Cylinder/Capsule）。</summary>
    private static GameObject BuildPrimitiveNode(GeometryElement geometry)
    {
        return geometry switch
        {
            BoxGeometry box => new Box(box.Size.X, box.Size.Y, box.Size.Z),
            SphereGeometry sphere => new Sphere(sphere.Radius),
            CylinderGeometry cylinder => new Cylinder(cylinder.Radius, cylinder.Length),
            CapsuleGeometry capsule => new Capsule(capsule.Radius, capsule.Length),
            MeshGeometry => throw new InvalidOperationException("mesh 几何应在外层处理。"),
            _ => throw new ArgumentOutOfRangeException(nameof(geometry)),
        };
    }

    /// <summary>把 URDF 材质元素转换为 CPU 材质描述：颜色 + 漫反射贴图引用。</summary>
    private MaterialData BuildMaterialData(MaterialElement? material)
    {
        var data = new MaterialData
        {
            BaseColor = material?.Color ?? new Vector4(0.78f, 0.78f, 0.78f, 1f),
        };
        if (material?.TextureFile is { } textureFile)
        {
            IAssetResolver resolver = _resolver ?? new FileSystemAssetResolver();
            if (resolver.Resolve(textureFile, _description.SourceBaseDirectory) is { } path)
                data.AlbedoTexture = TextureReference.FromFile(path);
        }

        return data;
    }

    /// <summary>把行主序位姿矩阵（仅旋转 + 平移）分解到 Transform 的 Position/Rotation（保留现有 Scale）。</summary>
    private static void ApplyPose(Transform transform, Matrix4x4 pose)
    {
        if (Matrix4x4.Decompose(pose, out _, out Quaternion rotation, out Vector3 position))
            transform.SetLocalPose(position, rotation, transform.Scale);
        else
            transform.SetLocalPose(pose.Translation, Quaternion.Identity, transform.Scale);
    }

    /// <summary>锁定整棵子树（含根）：之后任何直接写子 link Transform 的尝试都会抛异常。</summary>
    private void LockTree() => LockTransform(Transform);

    private static void LockTransform(Transform transform)
    {
        transform.SetReadOnly(true);
        foreach (Transform child in transform.Children)
            LockTransform(child);
    }
}