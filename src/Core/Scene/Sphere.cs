using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Sphere primitive (<see cref="GameObject"/> derived): constructs a UV sphere mesh.
/// <paramref name="segments"/> is the circumferential segment count, <paramref name="rings"/> the
/// latitude segment count.
/// </summary>
public sealed class Sphere : GameObject
{
    /// <summary>Sphere radius.</summary>
    public float Radius { get; }

    public Sphere(float radius, int segments = 32, int rings = 16, string? name = null)
        : base(Primitives.CreateSphere(radius, segments, rings), new MaterialData(), name ?? nameof(Sphere))
    {
        Radius = radius;
    }
}
