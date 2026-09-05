using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// CPU 侧点集数据（GL_POINTS），用于点云等无光照点显示。
/// 位置逐点存储；v1 颜色/点大小由挂载对象统一给定（后续可扩展逐点颜色属性）。
/// </summary>
public sealed class PointCloudData
{
    private readonly List<Vector3> _positions = new();

    /// <summary>点大小（像素），渲染时作为 uniform 传入。</summary>
    public float PointSize { get; set; } = 3f;

    /// <summary>点数。</summary>
    public int Count => _positions.Count;

    public IReadOnlyList<Vector3> Positions => _positions;

    /// <summary>批量设置点集（替换现有内容）。</summary>
    public void SetPoints(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        _positions.Clear();
        _positions.AddRange(points);
    }

    /// <summary>导出连续位置数组（每点 xyz），供渲染后端上传 GL_POINTS。</summary>
    public float[] ToPositionArray()
    {
        var result = new float[_positions.Count * 3];
        int i = 0;
        foreach (Vector3 p in _positions)
        {
            result[i++] = p.X;
            result[i++] = p.Y;
            result[i++] = p.Z;
        }
        return result;
    }
}
