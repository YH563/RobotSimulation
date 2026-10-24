using System.Numerics;

namespace RobotSimulation.Robot.Description;

/// <summary>
/// A collision element of a link: geometry plus its pose relative to the link frame. ↔ URDF
/// &lt;link&gt;&lt;collision&gt;. Collision materials and physical coefficients are not part of this
/// visualization/collision-query model.
/// </summary>
public sealed record CollisionElement
{
    /// <summary>Optional collision name from the URDF.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Pose of the geometry relative to the owning link frame. ↔ URDF collision.origin (xyz/rpy combined).
    /// </summary>
    public Matrix4x4 LocalTransform { get; init; } = Matrix4x4.Identity;

    /// <summary>Geometry shape.</summary>
    public required GeometryElement Geometry { get; init; }
}
