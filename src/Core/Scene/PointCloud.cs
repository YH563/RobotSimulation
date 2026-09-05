using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 点云（<see cref="GameObject"/>，Point 通道）：显示一批三维点。v1 使用单色 + 统一点大小；
/// 数据更新走 <see cref="SetPoints"/>（每次替换内部 <see cref="PointCloudData"/> 并重新上传）。
/// </summary>
public sealed class PointCloud : GameObject
{
    private float _pointSize;

    /// <summary>点大小（像素）。</summary>
    public float PointSize
    {
        get => _pointSize;
        set => _pointSize = value;
    }

    public PointCloud(float pointSize = 3f, Vector4? color = null, string? name = null)
        : base(null, null, name ?? nameof(PointCloud))
    {
        _pointSize = pointSize;
        MaterialData = new MaterialData
        {
            PassKind = RenderPassKind.Point,
            BaseColor = color ?? new Vector4(0.95f, 0.4f, 0.2f, 1f),
        };
        PointData = new PointCloudData { PointSize = pointSize };
    }

    /// <summary>替换点集内容（每次调用产生新的 CPU 数据并触发一次 GPU 上传）。</summary>
    public void SetPoints(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        var data = new PointCloudData { PointSize = PointSize };
        data.SetPoints(points);
        PointData = data;
    }
}
