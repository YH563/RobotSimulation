using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using RobotSimulation.Core.GameObjects;
using RobotSimulation.Robot.Description;
using RobotSimulation.Robot.State;
using RobotSimulation.Robot.Urdf;

namespace RobotSimulation.Robot;

/// <summary>
/// 机器人的单一入口（M3）：一个对象同时负责
///  1) 解析 URDF 并持有纯数据描述 <see cref="Description"/>（可 headless 使用）；
///  2) 创建可加入场景的 <see cref="RobotGameObject"/>——一棵树状 GameObject（link 是其
///     子对象），纯数据、无任何渲染参数，由调用方 scene.Add 后即被渲染器遍历绘制。
///
/// 使用示例：
/// <code>
/// var model   = RobotModel.ParseFile("arm.urdf");     // ① 解析 → 描述
/// var robotGo = model.Instantiate();                  // ② 创建场景对象（纯数据，任意线程）
/// scene.Add(robotGo);                                  // ③ 与其它 GameObject 平等加入场景
/// robotGo.SetJointValue("shoulder", 1.2f);           // ④ 驱动关节
/// </code>
/// </summary>
public sealed class RobotModel
{
    private readonly RobotDescription _description;
    private readonly IAssetResolver? _resolver;

    private RobotModel(RobotDescription description, IAssetResolver? resolver)
    {
        _description = description ?? throw new ArgumentNullException(nameof(description));
        _resolver = resolver;
    }

    // ------------------------------------------------------------------
    // ① 解析：URDF → 描述
    // ------------------------------------------------------------------

    /// <summary>解析内存中的 URDF 文本。</summary>
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

    /// <summary>读取 URDF 文件并解析；文件目录自动作为相对资源路径的基准。</summary>
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
    // 描述对象（headless / FK / 序列化）
    // ------------------------------------------------------------------

    /// <summary>解析得到的纯数据描述。</summary>
    public RobotDescription Description => _description;

    /// <summary>机器人名称（URDF &lt;robot name&gt;）。</summary>
    public string? Name => _description.Name;

    /// <summary>资源路径解析器（MeshGeometry 导入时使用）。</summary>
    public IAssetResolver? AssetResolver => _resolver;

    /// <summary>创建一个可驱动关节并计算 FK 的纯状态对象（不依赖渲染）。</summary>
    public RobotState CreateState() => new RobotState(_description);

    // ------------------------------------------------------------------
    // ② 创建场景对象（纯数据：MeshData + MaterialData，无任何渲染参数）
    // ------------------------------------------------------------------

    /// <summary>
    /// 把描述构建为场景中的 <see cref="RobotGameObject"/>（根 link 骨架节点即它本身）。
    /// 结构：每 link 一个骨架 GameObject，visual 为其携带模型数据/材质描述的子节点；
    /// 关节把子树挂到父 link 之下并施加 joint.origin。要求描述为单根。
    /// 不含任何 GPU/渲染概念——网格与材质的 GPU 实例化由渲染器在绘制时完成。
    /// </summary>
    public RobotGameObject Instantiate()
    {
        if (_description.RootLinks.Count != 1)
            throw new ArgumentException(
                $"机器人 '{_description.Name}' 的根 link 数量为 {_description.RootLinks.Count}，" +
                "作为单个 GameObject 构建需要恰好 1 个根。",
                nameof(_description));

        string rootLinkName = _description.RootLinks[0].Name;
        var robotObject = new RobotGameObject(rootLinkName, _description.Name);

        // 1) 每个 link 一个骨架节点（根 = robotObject 本身）
        var linkNodes = new Dictionary<string, GameObject>(StringComparer.Ordinal) { [rootLinkName] = robotObject };
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
                GameObject visualNode = BuildVisualNode(link.Name, i, link.VisualElements[i]);
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
        robotObject.ConfigureDriving(_description, linkNodes);

        return robotObject;
    }

    private GameObject BuildVisualNode(string linkName, int index, VisualElement visual)
    {
        var node = new GameObject(null, null, $"{linkName}:visual{index}");

        if (visual.Geometry is MeshGeometry mesh)
        {
            // mesh 文件加载属 Core/GameObjects/Import（M2.5），此处暂不支持并提示
            Console.WriteLine(
                $"[RobotModel] 警告：visual '{node.Name}' 引用 mesh '{mesh.Uri}'，" +
                "mesh 加载尚未实现，已跳过该 visual。");
            node.Visible = false;
            return node;
        }

        node.MeshData = BuildMeshData(visual.Geometry);
        node.MaterialData = BuildMaterialData(visual.Material);
        ApplyPose(node.Transform, visual.LocalTransform);
        return node;
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

    private static MeshData BuildMeshData(GeometryElement geometry)
    {
        return geometry switch
        {
            BoxGeometry box => PrimitiveBuilder.CreateBox(box.Size.X, box.Size.Y, box.Size.Z),
            SphereGeometry sphere => PrimitiveBuilder.CreateSphere(sphere.Radius),
            CylinderGeometry cylinder => PrimitiveBuilder.CreateCylinder(cylinder.Radius, cylinder.Length),
            CapsuleGeometry capsule => PrimitiveBuilder.CreateCapsule(capsule.Radius, capsule.Length),
            MeshGeometry => throw new InvalidOperationException("mesh 几何应在外层处理。"),
            _ => throw new ArgumentOutOfRangeException(nameof(geometry)),
        };
    }

    private static void ApplyPose(Transform transform, Matrix4x4 pose)
    {
        // 位姿仅含旋转+平移（无缩放），Decompose 恒可成功
        if (Matrix4x4.Decompose(pose, out _, out Quaternion rotation, out Vector3 position))
        {
            transform.Position = position;
            transform.Rotation = rotation;
        }
        else
        {
            transform.Position = pose.Translation;
            transform.Rotation = Quaternion.Identity;
        }
    }
}
