using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 球体基础图元（<see cref="GameObject"/> 派生）：构造即生成经纬球（UV 球）网格。
/// <paramref name="segments"/> 为圆周分段数，<paramref name="rings"/> 为纬线分段数。
/// </summary>
public sealed class Sphere : GameObject
{
    /// <summary>球半径。</summary>
    public float Radius { get; }

    public Sphere(float radius, int segments = 32, int rings = 16, string? name = null)
        : base(Primitives.CreateSphere(radius, segments, rings), new MaterialData(), name ?? nameof(Sphere))
    {
        Radius = radius;
    }
}
