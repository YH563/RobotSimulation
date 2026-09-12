using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Capsule primitive (<see cref="GameObject"/> derived): constructs a capsule mesh.
/// Its revolution axis is local +Z (URDF/ROS semantics); <c>length</c> is the middle
/// cylinder segment length (excluding the hemispherical caps), so total height = length + 2 × radius.
/// </summary>
public sealed class Capsule : GameObject
{
    /// <summary>Radius (including the hemispherical caps).</summary>
    public float Radius { get; }

    /// <summary>Middle cylinder segment length (excluding caps, in meters).</summary>
    public float Length { get; }

    /// <summary>Creates a capsule mesh object whose revolution axis is local +Z.</summary>
    /// <param name="radius">Radius of the body and of the hemispherical caps.</param>
    /// <param name="length">Length of the middle cylinder segment (total height = length + 2 × radius).</param>
    /// <param name="segments">Circumferential segment count.</param>
    /// <param name="rings">Latitude segment count of each cap.</param>
    /// <param name="name">Scene object name (defaults to <c>Capsule</c>).</param>
    public Capsule(float radius, float length, int segments = 32, int rings = 8, string? name = null)
        : base(Primitives.CreateCapsule(radius, length, segments, rings), new MaterialData(), name ?? nameof(Capsule))
    {
        Radius = radius;
        Length = length;
    }
}
