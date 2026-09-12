using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Cylinder primitive (<see cref="GameObject"/> derived): constructs a cylinder mesh.
/// Its revolution axis is local +Z (URDF/ROS semantics); <c>segments</c> is the
/// circumferential segment count.
/// </summary>
public sealed class Cylinder : GameObject
{
    /// <summary>Radius of the base/top faces.</summary>
    public float Radius { get; }

    /// <summary>Cylinder height (along Z, in meters).</summary>
    public float Length { get; }

    /// <summary>Creates a cylinder mesh object whose revolution axis is local +Z.</summary>
    /// <param name="radius">Radius of the base and top faces.</param>
    /// <param name="length">Height along Z.</param>
    /// <param name="segments">Circumferential segment count.</param>
    /// <param name="name">Scene object name (defaults to <c>Cylinder</c>).</param>
    public Cylinder(float radius, float length, int segments = 32, string? name = null)
        : base(Primitives.CreateCylinder(radius, length, segments), new MaterialData(), name ?? nameof(Cylinder))
    {
        Radius = radius;
        Length = length;
    }
}
