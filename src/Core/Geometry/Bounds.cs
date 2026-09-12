using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Axis-aligned bounding box (AABB), expressed by its min/max corners. Mesh-local
/// coordinates, cameras, etc. use world Z-up. Bounds only describes "a box region",
/// with no coordinate-system semantics; used for ray broad-phase and hit queries.
/// </summary>
public readonly struct Bounds
{
    /// <summary>Corner with the smallest coordinate on every axis.</summary>
    public Vector3 Min { get; }

    /// <summary>Corner with the largest coordinate on every axis.</summary>
    public Vector3 Max { get; }

    /// <summary>Center of the box.</summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>Size of the box (Max - Min).</summary>
    public Vector3 Size => Max - Min;

    /// <summary>
    /// Creates a box from its two corners. There is no validation: <paramref name="min"/> is expected to be
    /// component-wise less than or equal to <paramref name="max"/> (use <see cref="FromPoints"/> to derive
    /// both corners from actual data).
    /// </summary>
    /// <param name="min">Minimum corner.</param>
    /// <param name="max">Maximum corner.</param>
    public Bounds(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Computes a bounding box from a set of points; returns an empty box (Min = Max = 0) when <paramref name="points"/> is empty.</summary>
    public static Bounds FromPoints(System.Collections.Generic.IEnumerable<Vector3> points)
    {
        var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
            return new Bounds(Vector3.Zero, Vector3.Zero);

        Vector3 min = enumerator.Current, max = enumerator.Current;
        while (enumerator.MoveNext())
        {
            Vector3 p = enumerator.Current;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new Bounds(min, max);
    }
}
