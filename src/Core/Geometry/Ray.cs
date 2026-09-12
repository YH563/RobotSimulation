using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// A world-space ray: an origin plus a normalized direction. The direction is normalized at
/// construction, so the ray parameter <c>t</c> (the distances returned by <see cref="Raycast"/>)
/// is measured in world units and can be compared across rays and objects.
/// </summary>
public readonly struct Ray
{
    /// <summary>Ray origin (world coordinates).</summary>
    public Vector3 Origin { get; }

    /// <summary>Unit-length direction, always normalized by the constructor.</summary>
    public Vector3 Direction { get; }

    /// <param name="origin">Ray origin (world coordinates).</param>
    /// <param name="direction">Ray direction (any non-zero length; automatically normalized).</param>
    /// <exception cref="ArgumentException"><paramref name="direction"/> has near-zero length.</exception>
    public Ray(Vector3 origin, Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-12f)
            throw new ArgumentException("Ray direction cannot be a zero vector.", nameof(direction));

        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }
}
