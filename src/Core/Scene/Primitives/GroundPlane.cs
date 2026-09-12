using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Horizontal ground primitive (<see cref="GameObject"/> derived): lies on the XY plane (z = 0) with
/// normal +Z, width <c>width</c> (along X) and depth <c>depth</c> (along Y).
/// Used as the world ground of a Z-up engine (matching URDF/ROS). Named GroundPlane to avoid
/// clashing with System.Numerics.Plane.
/// </summary>
public sealed class GroundPlane : GameObject
{
    /// <summary>Width (along X).</summary>
    public float Width { get; }

    /// <summary>Depth (along Y).</summary>
    public float Depth { get; }

    /// <summary>Creates a horizontal plane centered on the local origin, facing +Z.</summary>
    /// <param name="width">Width along X.</param>
    /// <param name="depth">Depth along Y.</param>
    /// <param name="name">Scene object name (defaults to <c>GroundPlane</c>).</param>
    public GroundPlane(float width, float depth, string? name = null)
        : base(Primitives.CreatePlane(width, depth), new MaterialData(), name ?? nameof(GroundPlane))
    {
        Width = width;
        Depth = depth;
    }
}
