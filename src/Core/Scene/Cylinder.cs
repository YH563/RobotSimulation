using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 圆柱体基础图元（<see cref="GameObject"/> 派生）：构造即生成圆柱网格。
/// 回转轴沿局部 +Z（URDF/ROS 语义）；<paramref name="segments"/> 为圆周分段数。
/// </summary>
public sealed class Cylinder : GameObject
{
    /// <summary>底面/顶面半径。</summary>
    public float Radius { get; }

    /// <summary>圆柱高度（沿 Z，米）。</summary>
    public float Length { get; }

    public Cylinder(float radius, float length, int segments = 32, string? name = null)
        : base(Primitives.CreateCylinder(radius, length, segments), new MaterialData(), name ?? nameof(Cylinder))
    {
        Radius = radius;
        Length = length;
    }
}
