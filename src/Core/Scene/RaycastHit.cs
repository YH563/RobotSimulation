using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// <see cref="SceneGraph.Pick"/> 的一次命中结果（不可变）。只含数据，不持有 GPU/渲染资源；
/// 命中点为世界坐标，距离沿射线（世界单位），法线为世界法线，(U, V) 为命中三角形的重心坐标。
/// </summary>
public readonly struct RaycastHit
{
    /// <summary>被命中的对象（拥有该 mesh 的节点）。</summary>
    public GameObject Object { get; }

    /// <summary>世界坐标命中点。</summary>
    public Vector3 Point { get; }

    /// <summary>沿射线方向的距离（世界单位）。</summary>
    public float Distance { get; }

    /// <summary>世界坐标命中法线（已归一化，朝向射线的来向）。</summary>
    public Vector3 Normal { get; }

    /// <summary>命中三角形上，顶点 B 的插值权重（重心坐标第 1 个分量）。</summary>
    public float U { get; }

    /// <summary>命中三角形上，顶点 C 的插值权重（重心坐标第 2 个分量）。</summary>
    public float V { get; }

    public RaycastHit(GameObject @object, Vector3 point, float distance, Vector3 normal, float u, float v)
    {
        Object = @object;
        Point = point;
        Distance = distance;
        Normal = normal;
        U = u;
        V = v;
    }
}
