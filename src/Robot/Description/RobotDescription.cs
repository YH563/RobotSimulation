using System.Numerics;

namespace RobotSimulation.Robot.Description;

/// <summary>
/// Aggregate root of a robot's static description, parsed from URDF (or future import formats).
/// Pure data, zero rendering dependencies, serializable; holds no runtime state (joint angles, global
/// poses, etc. belong to the Robot/State layer). This description only covers visualization-relevant
/// information and ignores physical parameters like inertia.
/// </summary>
public sealed class RobotDescription
{
    /// <summary>Robot name. ↔ URDF &lt;robot name&gt;</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Directory of the URDF/model file (used to resolve relative mesh/texture paths later); null when
    /// parsed from memory with no base directory.
    /// </summary>
    public string? SourceBaseDirectory { get; init; }

    /// <summary>All links.</summary>
    public List<Link> Links { get; } = new();

    /// <summary>All joints.</summary>
    public List<Joint> Joints { get; } = new();

    /// <summary>
    /// Finds a link by name; returns null if not found.
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
    /// Top-level links: those not used as a child link by any joint.
    /// Multiple roots are allowed (legal in "partial assembly" cases, not an error); the caller decides how to handle them.
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
/// Joint type (the drivable subset for visualization).
/// </summary>
public enum JointType
{
    /// <summary>Fixed connection, not movable.</summary>
    Fixed,

    /// <summary>Revolute joint, rotates within limits. ↔ URDF revolute (with limit).</summary>
    Revolute,

    /// <summary>Continuous rotation (a revolute with no limit, such as a wheel). ↔ URDF continuous.</summary>
    Continuous,

    /// <summary>Prismatic joint. ↔ URDF prismatic.</summary>
    Prismatic,
}

/// <summary>
/// Static description of a joint: mounts the <see cref="ChildLinkName"/> frame under the
/// <see cref="ParentLinkName"/> frame. Only topology and fixed parameters; **the current joint position
/// etc. are runtime state belonging to the Robot/State layer**.
/// </summary>
public sealed record Joint
{
    /// <summary>Joint name. ↔ URDF &lt;joint name&gt;</summary>
    public required string Name { get; init; }

    /// <summary>Joint type.</summary>
    public required JointType Type { get; init; } = JointType.Fixed;

    /// <summary>Parent link name.</summary>
    public required string ParentLinkName { get; init; }

    /// <summary>Child link name.</summary>
    public required string ChildLinkName { get; init; }

    /// <summary>
    /// Pose of the child link frame relative to the parent link frame. ↔ URDF joint.origin (xyz/rpy combined).
    /// A rigid transform (translation + rotation only), row-vector/row-major consistent with Core.Scene.Transform.
    /// </summary>
    public Matrix4x4 Origin { get; init; } = Matrix4x4.Identity;

    /// <summary>
    /// Rotation/translation axis, defined in the child link frame. ↔ URDF &lt;axis&gt;;
    /// URDF defaults to (1,0,0). Only meaningful for Revolute/Continuous/Prismatic.
    /// </summary>
    public Vector3 Axis { get; init; } = Vector3.UnitX;
}

/// <summary>
/// Static description of a link.
/// A link has no "local origin" itself — its frame is determined by the joint connecting it
/// <see cref="Joint.Origin"/> (except root links, whose world placement belongs to the runtime/scene layer).
/// </summary>
public sealed record Link
{
    /// <summary>Link name. ↔ URDF &lt;link name&gt;</summary>
    public required string Name { get; init; }

    /// <summary>Visual elements mounted in this link's frame (a URDF link may have multiple visuals).</summary>
    public List<VisualElement> VisualElements { get; init; } = new();
}

/// <summary>
/// A single visual element: geometry + pose relative to the owning link's frame + optional material.
/// ↔ URDF &lt;link&gt;&lt;visual&gt;
/// </summary>
public sealed record VisualElement
{
    /// <summary>
    /// Pose of the geometry relative to the owning link's frame. ↔ URDF visual.origin (xyz/rpy combined).
    /// </summary>
    public Matrix4x4 LocalTransform { get; init; } = Matrix4x4.Identity;

    /// <summary>Geometry shape.</summary>
    public required GeometryElement Geometry { get; init; }

    /// <summary>
    /// Optional material; when null the rendering layer uses a default appearance.
    /// URDF materials referenced by name are resolved by the parser into definitions here.
    /// </summary>
    public MaterialElement? Material { get; init; }
}

/// <summary>
/// Material description (visualization info only). ↔ URDF &lt;material&gt;
/// </summary>
public sealed record MaterialElement
{
    /// <summary>Material name (kept as-is after reference resolution).</summary>
    public string? Name { get; init; }

    /// <summary>RGBA color, components in [0,1].</summary>
    public Vector4? Color { get; init; }

    /// <summary>Texture file reference (relative path / package://, resolved by the rendering layer); may be null.</summary>
    public string? TextureFile { get; init; }
}
