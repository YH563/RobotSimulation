using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 长方体基础图元（<see cref="GameObject"/> 派生）：构造即生成本地坐标的轴对齐盒网格。
/// 尺寸为 x/y/z 方向全宽（URDF &lt;box size&gt; 语义）；回转/轴不涉及。
/// </summary>
public sealed class Box : GameObject
{
    /// <summary>X 方向全宽。</summary>
    public float Width { get; }

    /// <summary>Y 方向全宽。</summary>
    public float Height { get; }

    /// <summary>Z 方向全宽。</summary>
    public float Depth { get; }

    public Box(float width, float height, float depth, string? name = null)
        : base(Primitives.CreateBox(width, height, depth), new MaterialData(), name ?? nameof(Box))
    {
        Width = width;
        Height = height;
        Depth = depth;
    }
}
