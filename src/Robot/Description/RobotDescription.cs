using System.Numerics;

namespace RobotSimulation.Robot.Description;

/// <summary>
/// 机器人静态描述的聚合根：由 URDF（或未来其它导入格式）解析得到。
/// 纯数据、零渲染依赖、可序列化；不含任何运行状态（关节角、全局位姿等属
/// Robot/State 层）。本描述只覆盖可视化所需信息，不处理惯性等物理参数。
/// </summary>
public sealed class RobotDescription
{
    /// <summary>机器人名称。↔ URDF &lt;robot name&gt;</summary>
    public string? Name { get; init; }

    /// <summary>
    /// URDF/模型文件所在目录（用于后续解析 mesh/texture 等相对路径）；内存解析且无基准目录时为 null。
    /// </summary>
    public string? SourceBaseDirectory { get; init; }

    /// <summary>全部连杆。</summary>
    public List<Link> Links { get; } = new();

    /// <summary>全部关节。</summary>
    public List<Joint> Joints { get; } = new();

    /// <summary>
    /// 按名称查找连杆；未找到返回 null。
    /// </summary>
    public Link? FindLink(string name)
    {
        foreach (var link in Links)
        {
            if (link.Name == name) return link;
        }

        return null;
    }

    /// <summary>
    /// 顶层连杆：没有任何关节以其为 child link。
    /// 可能出现多个根（"部分装配"场景合法，不视为错误），由调用方决定如何处理。
    /// </summary>
    public IReadOnlyList<Link> RootLinks
    {
        get
        {
            var childNames = new HashSet<string>();
            foreach (var joint in Joints)
                childNames.Add(joint.ChildLinkName);

            var roots = new List<Link>();
            foreach (var link in Links)
            {
                if (!childNames.Contains(link.Name))
                    roots.Add(link);
            }

            return roots;
        }
    }
}

/// <summary>
/// 关节类型（可视化可驱动的子集）。
/// </summary>
public enum JointType
{
    /// <summary>固定连接，不可动。</summary>
    Fixed,

    /// <summary>旋转关节，限位内转动。↔ URDF revolute（含 limit）。</summary>
    Revolute,

    /// <summary>连续旋转（无 limit 的 revolute，如轮子）。↔ URDF continuous。</summary>
    Continuous,

    /// <summary>平移关节。↔ URDF prismatic。</summary>
    Prismatic,
}

/// <summary>
/// 关节的静态描述：把 <see cref="ChildLinkName"/> 坐标系挂到
/// <see cref="ParentLinkName"/> 坐标系之下。
/// 只含拓扑与固定参数；**当前关节位置等运行状态属 Robot/State 层**。
/// </summary>
public sealed record Joint
{
    /// <summary>关节名称。↔ URDF &lt;joint name&gt;</summary>
    public required string Name { get; init; }

    /// <summary>关节类型。</summary>
    public required JointType Type { get; init; } = JointType.Fixed;

    /// <summary>父连杆名称。</summary>
    public required string ParentLinkName { get; init; }

    /// <summary>子连杆名称。</summary>
    public required string ChildLinkName { get; init; }

    /// <summary>
    /// 子 link 坐标系相对父 link 坐标系的位姿。↔ URDF joint.origin（xyz/rpy 合成）。
    /// 刚体变换（仅平移 + 旋转），行向量/行主序与 Core.GameObjects.Transform 一致。
    /// </summary>
    public Matrix4x4 Origin { get; init; } = Matrix4x4.Identity;

    /// <summary>
    /// 旋转/平移轴，定义在子 link 坐标系内。↔ URDF &lt;axis&gt;；
    /// URDF 缺省为 (1,0,0)。仅对 Revolute/Continuous/Prismatic 有意义。
    /// </summary>
    public Vector3 Axis { get; init; } = Vector3.UnitX;
}

/// <summary>
/// 连杆的静态描述。
/// 连杆自身没有"本地原点"——其坐标系由连接它的关节 <see cref="Joint.Origin"/> 决定
/// （根 link 除外，它的世界放置属运行/场景层）。
/// </summary>
public sealed record Link
{
    /// <summary>连杆名称。↔ URDF &lt;link name&gt;</summary>
    public required string Name { get; init; }

    /// <summary>挂在本连杆坐标系下的可视元素列表（URDF 中一个 link 可有多个 visual）。</summary>
    public List<VisualElement> VisualElements { get; init; } = new();
}

/// <summary>
/// 单个可视元素：几何 + 相对所属 link 坐标系的位姿 + 可选材质。
/// ↔ URDF &lt;link&gt;&gt;&lt;visual&gt;
/// </summary>
public sealed record VisualElement
{
    /// <summary>
    /// 几何相对所属 link 坐标系的位姿。↔ URDF visual.origin（xyz/rpy 合成）。
    /// </summary>
    public Matrix4x4 LocalTransform { get; init; } = Matrix4x4.Identity;

    /// <summary>几何形状。</summary>
    public required GeometryElement Geometry { get; init; }

    /// <summary>
    /// 可选材质；为 null 时渲染层使用默认外观。
    /// URDF 中按 name 引用的 robot 级材质，由解析器解析为定义后填入。
    /// </summary>
    public MaterialElement? Material { get; init; }
}

/// <summary>
/// 材质描述（仅可视化信息）。↔ URDF &lt;material&gt;
/// </summary>
public sealed record MaterialElement
{
    /// <summary>材质名称（用于引用解析后保留原名）。</summary>
    public string? Name { get; init; }

    /// <summary>RGBA 颜色，分量范围 [0,1]。</summary>
    public Vector4? Color { get; init; }

    /// <summary>贴图文件引用（相对路径/package://，渲染层解析）；可为 null。</summary>
    public string? TextureFile { get; init; }
}