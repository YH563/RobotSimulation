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
/// URDF text → <see cref="RobotDescription"/> parser (internal type). Used via the <see cref="RobotModel"/> facade.
///
/// Responsibility and scope:
///  - Visualization only: robot / link / visual / geometry / material / joint (topology);
///  - Ignores physical and extension content such as inertial / collision / transmission / gazebo;
///  - Poses are unified into row-major matrices (consistent with Core.Scene.Transform);
///  - All numbers are parsed InvariantCulture (URDF decimal point is always '.').
/// </summary>
internal sealed class UrdfParser
{
    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    /// <summary>
    /// Parses an XML document. Does not access the disk.
    /// </summary>
    public RobotDescription Parse(XDocument document, string? baseDirectory = null)
    {
        XElement? root = document.Root;
        if (root is null)
            throw new UrdfParseException("URDF document is empty (no root element).");
        if (root.Name.LocalName != "robot")
            throw new UrdfParseException(
                $"URDF root element should be &lt;robot&gt;, but is &lt;{root.Name.LocalName}&gt;.");

        var robot = new RobotDescription
        {
            Name = (string?)root.Attribute("name"),
            SourceBaseDirectory = baseDirectory,
        };

        // First pass: register robot-level material definitions (material name → definition).
        var materials = new Dictionary<string, MaterialElement>(StringComparer.Ordinal);
        foreach (XElement materialElement in Children(root, "material"))
            RegisterMaterial(materialElement, materials);

        // Second pass: parse link / joint; unknown elements (e.g. gazebo extensions) are ignored for forward compatibility.
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
                    break; // Already handled in the first pass, or explicitly ignored.

                default:
                    break; // Ignore unknown elements.
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

        // inertial / collision are explicitly ignored (this layer only handles visualization).
        return link;
    }

    private static VisualElement ParseVisual(XElement element, Dictionary<string, MaterialElement> materials)
    {
        XElement? geometryElement = Child(element, "geometry");
        if (geometryElement is null)
            throw new UrdfParseException(
                $"link's &lt;visual&gt; lacks &lt;geometry&gt; (in link '{element.Parent?.Attribute("name")?.Value}').");

        var visual = new VisualElement
        {
            LocalTransform = ParseOrigin(Child(element, "origin")),
            Geometry = ParseGeometry(geometryElement),
            Material = ParseVisualMaterial(Child(element, "material"), materials),
        };

        return visual;
    }

    /// <summary>
    /// Parses a visual material. Returns:
    ///  - An inline full definition (with color/texture) → construct and register (later same-name overrides);
    ///  - A name-only reference → look up the registered definition, warn and return null on a miss (render uses default appearance).
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
                return null; // No name and no content: equivalent to no material.
            if (materials.TryGetValue(name, out MaterialElement? existing))
                return existing;

            Logger.Warning($"visual references an undefined material '{name}'; using default appearance.");
            return null;
        }

        MaterialElement material = BuildMaterial(name, element);
        if (name is not null)
            materials[name] = material; // A later same-name definition overrides.
        return material;
    }

    private static void RegisterMaterial(XElement element, Dictionary<string, MaterialElement> materials)
    {
        string? name = (string?)element.Attribute("name");
        if (name is null)
            return;

        bool hasDefinition = element.Elements().Any(e => e.Name.LocalName == "color" || e.Name.LocalName == "texture");
        if (!hasDefinition)
            return; // A pure placeholder reference, registered by a later definition.

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
            throw new UrdfParseException("&lt;geometry&gt; lacks a geometry child element (box/sphere/cylinder/capsule/mesh).");

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
                throw new UrdfParseException($"Unsupported or unknown geometry type &lt;{shape.Name.LocalName}&gt;.");
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
                    $"joint '{element.Attribute("name")?.Value}' has unsupported type '{raw}'" +
                    "(this layer is visualization-only; drivable joints are limited to fixed/revolute/continuous/prismatic).");
            default:
                throw new UrdfParseException(
                    $"joint '{element.Attribute("name")?.Value}' has unknown type '{raw}'.");
        }
    }

    private static string RequiredChildLinkName(XElement jointElement, string childTag)
    {
        XElement? child = Child(jointElement, childTag);
        if (child is null)
            throw new UrdfParseException(
                $"joint '{jointElement.Attribute("name")?.Value}' lacks &lt;{childTag} link=\"...\"&gt;.");
        return RequiredAttribute(child, "link");
    }

    // ------------------------------------------------------------------
    // Generic number / attribute reading
    // ------------------------------------------------------------------

    private static Vector3 ParseAxis(XElement? element)
    {
        // URDF spec: axis defaults to (1,0,0). The description-layer default UnitX matches this.
        if (element is null)
            return Vector3.UnitX;

        Vector3 axis = ReadVector3(element, "xyz");
        float lengthSquared = axis.LengthSquared();
        if (lengthSquared < 1e-12f)
            throw new UrdfParseException("&lt;axis&gt; has length 0, so no rotation/translation direction can be determined.");
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

        // URDF origin is fixed-axis XYZ: rotate roll about world X, then pitch about world Y, then yaw
        // about world Z — i.e. quaternion composition q = qZ(yaw) * qY(pitch) * qX(roll) (order: roll → pitch → yaw).
        // Note: do not use Quaternion.CreateFromYawPitchRoll — its axis convention differs from URDF (in
        // practice it mistakenly treats roll as a rotation about Z); construct each axis explicitly here.
        var rotation = Matrix4x4.CreateFromQuaternion(
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rpy.Z)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, rpy.Y)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rpy.X));

        // Row-major convention: consistent with Core.Scene.Transform, rotate then translate.
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
            throw InvalidValue(element, attributeName, raw, "requires 3 space-separated numbers (x y z)");

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
            throw InvalidValue(element, attributeName, raw, "requires 4 space-separated numbers (r g b a)");

        return new Vector4(
            ParseSingle(parts[0], element, attributeName),
            ParseSingle(parts[1], element, attributeName),
            ParseSingle(parts[2], element, attributeName),
            ParseSingle(parts[3], element, attributeName));
    }

    private static float ParseSingle(string text, XElement element, string attributeName)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            throw InvalidValue(element, attributeName, text, "invalid number");
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
            $"element &lt;{element.Name.LocalName}&gt; lacks the required attribute '{attributeName}'.");

    private static UrdfParseException InvalidValue(XElement element, string attributeName, string raw, string reason)
        => new UrdfParseException(
            $"element &lt;{element.Name.LocalName}&gt; attribute '{attributeName}' has invalid value '{raw}': {reason}.");

    // ------------------------------------------------------------------
    // Element traversal helpers (by LocalName, tolerant of namespace/prefix)
    // ------------------------------------------------------------------

    private static IEnumerable<XElement> Children(XElement element, string localName)
        => element.Elements().Where(e => e.Name.LocalName == localName);

    private static XElement? Child(XElement element, string localName)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    // ------------------------------------------------------------------
    // Topology validation
    // ------------------------------------------------------------------

    private static void Validate(RobotDescription robot)
    {
        var linkNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Link link in robot.Links)
        {
            if (!linkNames.Add(link.Name))
                throw new UrdfParseException($"Duplicate link name '{link.Name}'.");
        }

        var jointNames = new HashSet<string>(StringComparer.Ordinal);
        var parentByChildLink = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Joint joint in robot.Joints)
        {
            if (!jointNames.Add(joint.Name))
                throw new UrdfParseException($"Duplicate joint name '{joint.Name}'.");
            if (!linkNames.Contains(joint.ParentLinkName))
                throw new UrdfParseException(
                    $"joint '{joint.Name}' parent link '{joint.ParentLinkName}' does not exist.");
            if (!linkNames.Contains(joint.ChildLinkName))
                throw new UrdfParseException(
                    $"joint '{joint.Name}' child link '{joint.ChildLinkName}' does not exist.");
            if (parentByChildLink.ContainsKey(joint.ChildLinkName))
                throw new UrdfParseException(
                    $"link '{joint.ChildLinkName}' is the child of multiple joints — URDF must be a tree.");
            parentByChildLink[joint.ChildLinkName] = joint.ParentLinkName;
        }

        // Cycle detection: walk the "child → parent" chain upward; a repeated node along the way is a cycle.
        foreach (string linkName in linkNames)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = linkName;
            while (parentByChildLink.TryGetValue(current, out string? parent))
            {
                if (!visited.Add(current))
                    throw new UrdfParseException(
                        $"Joint topology has a cycle: link '{current}' was revisited in the chain.");
                current = parent;
            }
        }
    }
}
