using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>光源类型。</summary>
public enum LightType
{
    /// <summary>点光源：向四周发光，位置由 Transform.Position 决定。</summary>
    Point,

    /// <summary>方向光：模拟无限远光源（如太阳），只沿一个方向照射，位置无关。</summary>
    Directional,
}

/// <summary>
/// 场景光源（特殊 GameObject）。不携带 Mesh/Material，因此不参与普通绘制，
/// 由渲染器每帧从场景收集其参数（类型/颜色/强度/位置）传入 shader。
/// 场景支持多个光源（见 <see cref="SceneGraph.Lights"/>）。
/// </summary>
public class Light : GameObject
{
    /// <summary>光源类型。</summary>
    public LightType Type { get; set; } = LightType.Point;

    /// <summary>光源颜色（RGB，分量范围 [0,1]，可为 &gt;1 表示高亮）。</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>光强系数。</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>光照有效半径（点光源衰减用；&lt;=0 表示不衰减）。</summary>
    public float Range { get; set; }

    /// <summary>方向光的照射方向（世界坐标，指向光源照射方向）；点光源忽略。</summary>
    public Vector3 Direction { get; set; } = -Vector3.UnitZ;

    /// <summary>点光源世界位置（即 Transform 位置）。</summary>
    public Vector3 Position => Transform.Position;

    public Light(string? name = "Light") : base(null, null, name)
    {
    }
}