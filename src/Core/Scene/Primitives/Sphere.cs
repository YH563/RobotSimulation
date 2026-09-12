using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Sphere primitive (<see cref="GameObject"/> derived): constructs a UV sphere mesh.
/// <c>segments</c> is the circumferential segment count, <c>rings</c> the
/// latitude segment count.
/// </summary>
public sealed class Sphere : GameObject
{
    /// <summary>Sphere radius.</summary>
    public float Radius { get; }

    /// <summary>Creates a UV sphere mesh object centered on the local origin.</summary>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="segments">Circumferential segment count.</param>
    /// <param name="rings">Latitude segment count.</param>
    /// <param name="name">Scene object name (defaults to <c>Sphere</c>).</param>
    public Sphere(float radius, int segments = 32, int rings = 16, string? name = null)
        : base(Primitives.CreateSphere(radius, segments, rings), new MaterialData(), name ?? nameof(Sphere))
    {
        Radius = radius;
    }
}
