using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 水平地面基础图元（<see cref="GameObject"/> 派生）：位于 XY 平面（z = 0）、法线朝 +Z，
/// 宽 <paramref name="width"/>（沿 X）、深 <paramref name="depth"/>（沿 Y）。
/// 用于 Z-up 引擎的世界地面（与 URDF/ROS 约定一致）。
/// 命名避开 System.Numerics.Plane，故为 GroundPlane。
/// </summary>
public sealed class GroundPlane : GameObject
{
    /// <summary>宽度（沿 X）。</summary>
    public float Width { get; }

    /// <summary>深度（沿 Y）。</summary>
    public float Depth { get; }

    public GroundPlane(float width, float depth, string? name = null)
        : base(Primitives.CreatePlane(width, depth), new MaterialData(), name ?? nameof(GroundPlane))
    {
        Width = width;
        Depth = depth;
    }
}
