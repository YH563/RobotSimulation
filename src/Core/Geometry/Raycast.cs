using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 纯几何射线求交原语（不引用场景/渲染类型）。所有方法假设 <see cref="Ray.Direction"/> 已归一化，
/// 因此返回的距离即世界单位距离，可直接跨对象比较。用于拾取的粗筛与精确命中。
/// </summary>
public static class Raycast
{
    /// <summary>射线与球求交；返回最近的正向距离；未命中返回 null。</summary>
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

    /// <summary>射线与无限平面求交；返回正向距离；平行或在射线后方返回 null。</summary>
    public static float? HitPlane(in Ray ray, Vector3 point, Vector3 normal)
    {
        float denom = Vector3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f)
            return null;

        float t = Vector3.Dot(point - ray.Origin, normal) / denom;
        return t >= 0f ? t : null;
    }

    /// <summary>
    /// 射线与轴对齐包围盒求交（slab 法）；返回进入距离（起点在盒内时为负值，仍视为命中）；
    /// 未命中返回 null。用于拾取前的粗筛。
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
            return o >= min && o <= max;   // 平行于该轴：仅在包围该轴的厚度内才可能命中

        float t1 = (min - o) / d;
        float t2 = (max - o) / d;
        if (t1 > t2)
            (t1, t2) = (t2, t1);
        if (t1 > tmin) tmin = t1;
        if (t2 < tmax) tmax = t2;
        return tmin <= tmax;
    }

    /// <summary>
    /// 射线与三角形求交（Möller–Trumbore，双面命中：正面与背面都可命中，法线朝向射线来向）。
    /// 返回是否命中，并输出沿射线的距离、世界法线及命中点的重心坐标 (u, v)。
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
            normal = -normal;   // 背面命中：翻转法线使其朝向射线来向
        normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        return true;
    }
}
