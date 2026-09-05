using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 曲线 / 路径（<see cref="GameObject"/>，线条通道）：按给定的连续点串折线连接。
/// 单色线条（由 <see cref="MaterialData.BaseColor"/> 给定）；数据变化时调用
/// <see cref="SetPoints"/> 重建即可（渲染器按新的数据引用重新上传一次）。
/// </summary>
public sealed class Curve : GameObject
{
    public Curve(IEnumerable<Vector3> points, Vector4? color = null, string? name = null)
        : base(null, null, name ?? nameof(Curve))
    {
        MaterialData = new MaterialData
        {
            PassKind = RenderPassKind.Line,
            BaseColor = color ?? new Vector4(1f, 0.55f, 0.1f, 1f),
        };
        LineData = new LineData();
        SetPoints(points);
    }

    /// <summary>替换曲线点列（至少 2 点成线；不足则清空）。</summary>
    public void SetPoints(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        var lines = new LineData();
        Vector3? previous = null;
        foreach (Vector3 p in points)
        {
            if (previous.HasValue)
                lines.AddSegment(previous.Value, p);
            previous = p;
        }
        LineData = lines;
    }
}
