using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using RobotSimulation.Core.Utils;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// URDF 文本 → <see cref="RobotDescription"/> 的解析器（内部类型）。
/// 通过 <see cref="RobotModel"/> 门面使用。
///
/// 职责与范围：
///  - 只覆盖可视化：robot / link / visual / geometry / material / joint（拓扑）；
///  - 忽略 inertial / collision / transmission / gazebo 等物理与扩展内容；
///  - 位姿统一合成为行主序矩阵（与 Core.Scene.Transform 约定一致）；
///  - 数值一律 InvariantCulture 解析（URDF 小数点恒为 '.'）。
/// </summary>
internal sealed class UrdfParser
{
    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    /// <summary>
    /// 解析 XML 文档。不访问磁盘。
    /// </summary>
    public RobotDescription Parse(XDocument document, string? baseDirectory = null)
    {
        XElement? root = document.Root;
        if (root is null)
            throw new UrdfParseException("URDF 文档为空（无根元素）。");
        if (root.Name.LocalName != "robot")
            throw new UrdfParseException(
                $"URDF 根元素应为 &lt;robot&gt;，实际为 &lt;{root.Name.LocalName}&gt;。");

        var robot = new RobotDescription
        {
            Name = (string?)root.Attribute("name"),
            SourceBaseDirectory = baseDirectory,
        };

        // 第一遍：注册 robot 级材质定义（material name → 定义）
        var materials = new Dictionary<string, MaterialElement>(StringComparer.Ordinal);
        foreach (XElement materialElement in Children(root, "material"))
            RegisterMaterial(materialElement, materials);

        // 第二遍：解析 link / joint；未知元素（gazebo 等扩展）一律忽略以向前兼容
        foreach (XElement element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "link":
                    robot.Links.Add(ParseLink(element, materials));
                    break;

                case "joint":
                    robot.Joints.Add(ParseJoint(element));
                    break;

                case "material":
                case "gazebo":
                case "transmission":
                    break; // 已在第一遍处理，或明确忽略

                default:
                    break; // 忽略未知元素
            }
        }

        Validate(robot);
        return robot;
    }

    // ------------------------------------------------------------------
    // link / visual
    // ------------------------------------------------------------------

    private static Link ParseLink(XElement element, Dictionary<string, MaterialElement> materials)
    {
        var link = new Link { Name = RequiredAttribute(element, "name") };

        foreach (XElement visual in Children(element, "visual"))
            link.VisualElements.Add(ParseVisual(visual, materials));

        // inertial / collision 明确忽略（本层只管可视化）
        return link;
    }

    private static VisualElement ParseVisual(XElement element, Dictionary<string, MaterialElement> materials)
    {
        XElement? geometryElement = Child(element, "geometry");
        if (geometryElement is null)
            throw new UrdfParseException(
                $"link 的 &lt;visual&gt; 缺少 &lt;geometry&gt;（位于 link '{element.Parent?.Attribute("name")?.Value}'）。");

        var visual = new VisualElement
        {
            LocalTransform = ParseOrigin(Child(element, "origin")),
            Geometry = ParseGeometry(geometryElement),
            Material = ParseVisualMaterial(Child(element, "material"), materials),
        };

        return visual;
    }

    /// <summary>
    /// 解析 visual 材质。返回：
    ///  - 内嵌完整定义（含 color/texture）→ 构造并注册（同名覆盖）；
    ///  - 仅按 name 引用 → 查已注册定义，缺失则告警并返回 null（渲染用默认外观）。
    /// </summary>
    private static MaterialElement? ParseVisualMaterial(
        XElement? element, Dictionary<string, MaterialElement> materials)
    {
        if (element is null)
            return null;

        string? name = (string?)element.Attribute("name");
        bool hasInlineDefinition =
            element.Elements().Any(e => e.Name.LocalName == "color" || e.Name.LocalName == "texture");

        if (!hasInlineDefinition)
        {
            if (name is null)
                return null; // 无名字也无内容，等价于没有材质
            if (materials.TryGetValue(name, out MaterialElement? existing))
                return existing;

            Logger.Warning($"visual 引用了未定义的材质 '{name}'，将使用默认外观。");
            return null;
        }

        MaterialElement material = BuildMaterial(name, element);
        if (name is not null)
            materials[name] = material; // 同名后者覆盖
        return material;
    }

    private static void RegisterMaterial(XElement element, Dictionary<string, MaterialElement> materials)
    {
        string? name = (string?)element.Attribute("name");
        if (name is null)
            return;

        bool hasDefinition = element.Elements().Any(e => e.Name.LocalName == "color" || e.Name.LocalName == "texture");
        if (!hasDefinition)
            return; // 纯占位引用，由后续定义注册

        materials[name] = BuildMaterial(name, element);
    }

    private static MaterialElement BuildMaterial(string? name, XElement element)
    {
        Vector4? color = null;
        string? textureFile = null;

        XElement? colorElement = Child(element, "color");
        if (colorElement is not null)
            color = ReadVector4(colorElement, "rgba");

        XElement? textureElement = Child(element, "texture");
        if (textureElement is not null)
            textureFile = (string?)textureElement.Attribute("filename");

        return new MaterialElement { Name = name, Color = color, TextureFile = textureFile };
    }

    // ------------------------------------------------------------------
    // geometry
    // ------------------------------------------------------------------

    private static GeometryElement ParseGeometry(XElement element)
    {
        XElement? shape = element.Elements().FirstOrDefault();
        if (shape is null)
            throw new UrdfParseException("&lt;geometry&gt; 内缺少几何子元素（box/sphere/cylinder/capsule/mesh）。");

        switch (shape.Name.LocalName)
        {
            case "box":
                return new BoxGeometry(ReadVector3(shape, "size"));

            case "sphere":
                return new SphereGeometry(ReadFloat(shape, "radius"));

            case "cylinder":
                return new CylinderGeometry(ReadFloat(shape, "radius"), ReadFloat(shape, "length"));

            case "capsule":
                return new CapsuleGeometry(ReadFloat(shape, "radius"), ReadFloat(shape, "length"));

            case "mesh":
            {
                string filename = RequiredAttribute(shape, "filename");
                return new MeshGeometry(filename);
            }

            default:
                throw new UrdfParseException($"不支持或未知的几何类型 &lt;{shape.Name.LocalName}&gt;。");
        }
    }

    // ------------------------------------------------------------------
    // joint
    // ------------------------------------------------------------------

    private static Joint ParseJoint(XElement element)
    {
        var joint = new Joint
        {
            Name = RequiredAttribute(element, "name"),
            Type = ParseJointType(element),
            ParentLinkName = RequiredChildLinkName(element, "parent"),
            ChildLinkName = RequiredChildLinkName(element, "child"),
            Origin = ParseOrigin(Child(element, "origin")),
            Axis = ParseAxis(Child(element, "axis")),
        };

        return joint;
    }

    private static JointType ParseJointType(XElement element)
    {
        string raw = RequiredAttribute(element, "type");
        switch (raw)
        {
            case "fixed": return JointType.Fixed;
            case "revolute": return JointType.Revolute;
            case "continuous": return JointType.Continuous;
            case "prismatic": return JointType.Prismatic;
            case "floating":
            case "planar":
                throw new UrdfParseException(
                    $"joint '{element.Attribute("name")?.Value}' 的类型 '{raw}' 不受支持" +
                    "（本层仅可视化，可驱动关节限定为 fixed/revolute/continuous/prismatic）。");
            default:
                throw new UrdfParseException(
                    $"joint '{element.Attribute("name")?.Value}' 的类型 '{raw}' 未知。");
        }
    }

    private static string RequiredChildLinkName(XElement jointElement, string childTag)
    {
        XElement? child = Child(jointElement, childTag);
        if (child is null)
            throw new UrdfParseException(
                $"joint '{jointElement.Attribute("name")?.Value}' 缺少 &lt;{childTag} link=\"...\"&gt;。");
        return RequiredAttribute(child, "link");
    }

    // ------------------------------------------------------------------
    // 通用数值 / 属性读取
    // ------------------------------------------------------------------

    private static Vector3 ParseAxis(XElement? element)
    {
        // URDF 规范：axis 缺省为 (1,0,0)。描述层默认 UnitX 与此一致。
        if (element is null)
            return Vector3.UnitX;

        Vector3 axis = ReadVector3(element, "xyz");
        float lengthSquared = axis.LengthSquared();
        if (lengthSquared < 1e-12f)
            throw new UrdfParseException("&lt;axis&gt; 的长度为 0，无法确定旋转/平移方向。");
        return Vector3.Normalize(axis);
    }

    private static Matrix4x4 ParseOrigin(XElement? element)
    {
        if (element is null)
            return Matrix4x4.Identity;

        Vector3 xyz = element.Attribute("xyz") is null
            ? Vector3.Zero
            : ReadVector3(element, "xyz");

        Vector3 rpy = element.Attribute("rpy") is null
            ? Vector3.Zero
            : ReadVector3(element, "rpy");

        // URDF origin 为固定轴 XYZ：先绕世界 X 转 roll，再绕世界 Y 转 pitch，最后绕世界 Z 转 yaw，
        // 即四元数合成 q = qZ(yaw) * qY(pitch) * qX(roll)（应用顺序：roll → pitch → yaw）。
        // 注意：不能使用 Quaternion.CreateFromYawPitchRoll —— 其参数轴约定与 URDF 不一致
        // （实测会把 roll 误当成绕 Z 旋转），这里逐轴显式构造。
        var rotation = Matrix4x4.CreateFromQuaternion(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rpy.Z)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, rpy.Y)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rpy.X));

        // 行主序约定：与 Core.Scene.Transform 一致，先旋转后平移。
        return rotation * Matrix4x4.CreateTranslation(xyz);
    }

    private static float ReadFloat(XElement element, string attributeName)
    {
        string? raw = (string?)element.Attribute(attributeName);
        if (raw is null)
            throw MissingAttribute(element, attributeName);
        return ParseSingle(raw, element, attributeName);
    }

    private static Vector3 ReadVector3(XElement element, string attributeName)
    {
        string? raw = (string?)element.Attribute(attributeName);
        if (raw is null)
            throw MissingAttribute(element, attributeName);

        string[] parts = raw.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw InvalidValue(element, attributeName, raw, "需要 3 个空格分隔的数值（x y z）");

        return new Vector3(
            ParseSingle(parts[0], element, attributeName),
            ParseSingle(parts[1], element, attributeName),
            ParseSingle(parts[2], element, attributeName));
    }

    private static Vector4 ReadVector4(XElement element, string attributeName)
    {
        string? raw = (string?)element.Attribute(attributeName);
        if (raw is null)
            throw MissingAttribute(element, attributeName);

        string[] parts = raw.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            throw InvalidValue(element, attributeName, raw, "需要 4 个空格分隔的数值（r g b a）");

        return new Vector4(
            ParseSingle(parts[0], element, attributeName),
            ParseSingle(parts[1], element, attributeName),
            ParseSingle(parts[2], element, attributeName),
            ParseSingle(parts[3], element, attributeName));
    }

    private static float ParseSingle(string text, XElement element, string attributeName)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw InvalidValue(element, attributeName, text, "非法数值");
        return value;
    }

    private static string RequiredAttribute(XElement element, string attributeName)
    {
        string? value = (string?)element.Attribute(attributeName);
        if (string.IsNullOrWhiteSpace(value))
            throw MissingAttribute(element, attributeName);
        return value;
    }

    private static UrdfParseException MissingAttribute(XElement element, string attributeName)
        => new UrdfParseException(
            $"元素 &lt;{element.Name.LocalName}&gt; 缺少必选属性 '{attributeName}'。");

    private static UrdfParseException InvalidValue(XElement element, string attributeName, string raw, string reason)
        => new UrdfParseException(
            $"元素 &lt;{element.Name.LocalName}&gt; 的属性 '{attributeName}' 的值 '{raw}' 非法：{reason}。");

    // ------------------------------------------------------------------
    // 元素遍历辅助（按 LocalName，容忍命名空间/前缀）
    // ------------------------------------------------------------------

    private static IEnumerable<XElement> Children(XElement element, string localName)
        => element.Elements().Where(e => e.Name.LocalName == localName);

    private static XElement? Child(XElement element, string localName)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    // ------------------------------------------------------------------
    // 拓扑校验
    // ------------------------------------------------------------------

    private static void Validate(RobotDescription robot)
    {
        var linkNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Link link in robot.Links)
        {
            if (!linkNames.Add(link.Name))
                throw new UrdfParseException($"重复的 link 名称 '{link.Name}'。");
        }

        var jointNames = new HashSet<string>(StringComparer.Ordinal);
        var parentByChildLink = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Joint joint in robot.Joints)
        {
            if (!jointNames.Add(joint.Name))
                throw new UrdfParseException($"重复的 joint 名称 '{joint.Name}'。");
            if (!linkNames.Contains(joint.ParentLinkName))
                throw new UrdfParseException(
                    $"joint '{joint.Name}' 的父 link '{joint.ParentLinkName}' 不存在。");
            if (!linkNames.Contains(joint.ChildLinkName))
                throw new UrdfParseException(
                    $"joint '{joint.Name}' 的子 link '{joint.ChildLinkName}' 不存在。");
            if (parentByChildLink.ContainsKey(joint.ChildLinkName))
                throw new UrdfParseException(
                    $"link '{joint.ChildLinkName}' 被多个 joint 作为子 link——URDF 必须是树。");
            parentByChildLink[joint.ChildLinkName] = joint.ParentLinkName;
        }

        // 环检测：沿"子 → 父"链向上走，途经重复节点即成环
        foreach (string linkName in linkNames)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = linkName;
            while (parentByChildLink.TryGetValue(current, out string? parent))
            {
                if (!visited.Add(current))
                    throw new UrdfParseException(
                        $"joint 拓扑成环：链路中重复经过 link '{current}'。");
                current = parent;
            }
        }
    }
}