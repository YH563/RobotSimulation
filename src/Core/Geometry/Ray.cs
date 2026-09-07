using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 世界空间射线：一个起点 + 一个归一化方向。构造即归一化方向，因此
/// 射线参数 <c>t</c>（<see cref="Raycast"/> 各求交返回的距离）直接以"世界单位"计，
/// 可跨射线/跨对象比较。
/// </summary>
public readonly struct Ray
{
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }

    /// <param name="origin">射线起点（世界坐标）。</param>
    /// <param name="direction">射线方向（可为任意非零长度；构造时自动归一化）。</param>
    /// <exception cref="ArgumentException"><paramref name="direction"/> 长度接近 0。</exception>
    public Ray(Vector3 origin, Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-12f)
            throw new ArgumentException("射线方向不能为零向量。", nameof(direction));

        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }
}
