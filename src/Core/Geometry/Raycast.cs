using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Pure geometric ray-intersection primitives (no scene/rendering types). All methods assume
/// <see cref="Ray.Direction"/> is normalized, so the returned distances are world-unit distances
/// and can be compared across objects. Used for picking broad-phase and exact hits.
/// </summary>
public static class Raycast
{
    /// <summary>Ray vs. sphere; returns the nearest positive distance, or null on a miss.</summary>
    public static float? HitSphere(in Ray ray, Vector3 center, float radius)
    {
        Vector3 oc = ray.Origin - center;
        float b = Vector3.Dot(oc, ray.Direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f)
            return null;

        float sqrt = MathF.Sqrt(disc);
        float t = -b - sqrt;
        if (t < 0f)
            t = -b + sqrt;
        return t < 0f ? null : t;
    }

    /// <summary>Ray vs. infinite plane; returns the positive distance, or null when parallel or behind the ray.</summary>
    public static float? HitPlane(in Ray ray, Vector3 point, Vector3 normal)
    {
        float denom = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f)
            return null;

        float t = Vector3.Dot(point - ray.Origin, normal) / denom;
        return t >= 0f ? t : null;
    }

    /// <summary>
    /// Ray vs. axis-aligned bounding box (slab method); returns the entry distance (negative when
    /// the origin is inside the box, still a hit); returns null on a miss. Used as a broad-phase
    /// check before picking.
    /// </summary>
    public static float? HitAABB(in Ray ray, in Bounds bounds)
    {
        float tmin = 0f, tmax = float.MaxValue;
        Vector3 o = ray.Origin, d = ray.Direction;

        if (!AdvanceSlab(o.X, d.X, bounds.Min.X, bounds.Max.X, ref tmin, ref tmax)) return null;
        if (!AdvanceSlab(o.Y, d.Y, bounds.Min.Y, bounds.Max.Y, ref tmin, ref tmax)) return null;
        if (!AdvanceSlab(o.Z, d.Z, bounds.Min.Z, bounds.Max.Z, ref tmin, ref tmax)) return null;

        return tmax < 0f ? null : tmin;
    }

    private static bool AdvanceSlab(float o, float d, float min, float max, ref float tmin, ref float tmax)
    {
        if (MathF.Abs(d) < 1e-9f)
            return o >= min && o <= max;   // Parallel to this axis: only hit if within the slab thickness.

        float t1 = (min - o) / d;
        float t2 = (max - o) / d;
        if (t1 > t2)
            (t1, t2) = (t2, t1);
        if (t1 > tmin) tmin = t1;
        if (t2 < tmax) tmax = t2;
        return tmin <= tmax;
    }

    /// <summary>
    /// Ray vs. triangle (Möller–Trumbore, double-sided: both front and back faces can be hit,
    /// with the normal facing the ray). Returns whether it hit, and outputs the distance along the
    /// ray, the world normal, and the barycentric coordinates (u, v) of the hit point.
    /// </summary>
    public static bool HitTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c,
        out float distance, out Vector3 normal, out float u, out float v)
    {
        distance = 0f;
        normal = default;
        u = 0f;
        v = 0f;

        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 pv = Vector3.Cross(ray.Direction, e2);
        float det = Vector3.Dot(e1, pv);
        if (MathF.Abs(det) < 1e-9f)
            return false;

        float invDet = 1f / det;
        Vector3 tv = ray.Origin - a;

        u = Vector3.Dot(tv, pv) * invDet;
        if (u < 0f || u > 1f)
            return false;

        Vector3 qv = Vector3.Cross(tv, e1);
        v = Vector3.Dot(ray.Direction, qv) * invDet;
        if (v < 0f || u + v > 1f)
            return false;

        float t = Vector3.Dot(e2, qv) * invDet;
        if (t <= 0f)
            return false;

        distance = t;
        normal = Vector3.Cross(e1, e2);
        if (det < 0f)
            normal = -normal;   // Back-face hit: flip so the normal faces the ray.
        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        return true;
    }
}
