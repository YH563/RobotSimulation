using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// A single hit result from <see cref="SceneGraph.Pick"/> (immutable). Data only, holds no GPU/rendering
/// resources; the hit point is in world coordinates, the distance is along the ray (world units), the
/// normal is a world-space normal, and (U, V) are the barycentric coordinates of the hit triangle.
/// </summary>
public readonly struct RaycastHit
{
    /// <summary>The object that was hit (the node owning the mesh).</summary>
    public GameObject Object { get; }

    /// <summary>World-space hit point.</summary>
    public Vector3 Point { get; }

    /// <summary>Distance along the ray direction (world units).</summary>
    public float Distance { get; }

    /// <summary>World-space hit normal (normalized, facing toward the ray).</summary>
    public Vector3 Normal { get; }

    /// <summary>Interpolation weight for vertex B of the hit triangle (first barycentric component).</summary>
    public float U { get; }

    /// <summary>Interpolation weight for vertex C of the hit triangle (second barycentric component).</summary>
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
