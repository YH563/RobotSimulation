using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 轴对齐包围盒（AABB），用最小/最大角点表示。网格局部坐标、相机等都以世界 Z 向上为单位，
/// Bounds 只描述"一个长方体区域"，不含坐标系语义，供射线粗筛与命中查询使用。
/// </summary>
public readonly struct Bounds
{
    public Vector3 Min { get; }
    public Vector3 Max { get; }

    /// <summary>包围盒中心。</summary>
    public Vector3 Center => (Min + Max) * 0.5f;

    /// <summary>包围盒尺寸（Max - Min）。</summary>
    public Vector3 Size => Max - Min;

    public Bounds(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>由一组点计算包围盒；当 <paramref name="points"/> 为空时返回一个空盒（Min = Max = 0）。</summary>
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
