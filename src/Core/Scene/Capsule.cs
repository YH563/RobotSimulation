using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 胶囊体基础图元（<see cref="GameObject"/> 派生）：构造即生成胶囊网格。
/// 回转轴沿局部 +Z（URDF/ROS 语义）；<paramref name="length"/> 为中间圆柱段长度
/// （不含两端半球帽），总高 = length + 2 × radius。
/// </summary>
public sealed class Capsule : GameObject
{
    /// <summary>半径（含两端半球帽）。</summary>
    public float Radius { get; }

    /// <summary>中间圆柱段长度（不含半球帽，米）。</summary>
    public float Length { get; }

    public Capsule(float radius, float length, int segments = 32, int rings = 8, string? name = null)
        : base(Primitives.CreateCapsule(radius, length, segments, rings), new MaterialData(), name ?? nameof(Capsule))
    {
        Radius = radius;
        Length = length;
    }
}
