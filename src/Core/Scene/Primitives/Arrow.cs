using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Arrow (<see cref="GameObject"/>, Model pass): a solid arrow along local +Z
/// (tail z=0 … tip z=Length), made of a cylinder shaft plus a cone head combined into a single mesh.
/// Use a <see cref="Transform"/> to place/point it in any direction.
/// </summary>
public sealed class Arrow : GameObject
{
    /// <summary>Total arrow length.</summary>
    public float Length { get; }

    /// <summary>Creates an arrow mesh object pointing along local +Z, with the given color.</summary>
    /// <param name="length">Total length, tail at z=0 to tip at z=<paramref name="length"/>.</param>
    /// <param name="shaftRadius">Radius of the cylindrical shaft.</param>
    /// <param name="headRadius">Radius of the cone head (must exceed <paramref name="shaftRadius"/>).</param>
    /// <param name="headLength">Length of the cone head (must be positive and less than <paramref name="length"/>).</param>
    /// <param name="color">Base color; null uses the default orange.</param>
    /// <param name="segments">Circumferential segment count.</param>
    /// <param name="name">Scene object name (defaults to <c>Arrow</c>).</param>
    public Arrow(
        float length = 1f,
        float shaftRadius = 0.02f,
        float headRadius = 0.06f,
        float headLength = 0.22f,
        Vector4? color = null,
        int segments = 24,
        string? name = null)
        : base(
            CreateArrowMesh(length, shaftRadius, headRadius, headLength, segments),
            new MaterialData { BaseColor = color ?? new Vector4(1f, 0.62f, 0.1f, 1f) },
            name ?? nameof(Arrow))
    {
        Length = length;
    }

    /// <summary>
    /// Generates the arrow mesh data independently (for reuse, e.g. sharing one unit arrow across
    /// each object's local axes).
    /// </summary>
    public static MeshData CreateArrowMesh(
        float length = 1f,
        float shaftRadius = 0.02f,
        float headRadius = 0.06f,
        float headLength = 0.22f,
        int segments = 24)
        => BuildMesh(length, shaftRadius, headRadius, headLength, segments);

    private static MeshData BuildMesh(float length, float shaftRadius, float headRadius, float headLength, int segments)
    {
        if (length <= 0f || float.IsNaN(length) || float.IsInfinity(length))
            throw new ArgumentOutOfRangeException(nameof(length), length, "Arrow length must be a positive finite value.");
        if (headRadius <= shaftRadius)
            throw new ArgumentException("headRadius must be greater than shaftRadius (otherwise the cone head is invisible).", nameof(headRadius));
        if (headLength <= 0f || headLength >= length)
            throw new ArgumentOutOfRangeException(nameof(headLength), headLength, "headLength must be greater than 0 and less than the total length.");

        float shaftLen = length - headLength;

        // Cylinder shaft (CreateCylinder axis along +Z, spanning -l/2..+l/2), lifted so the tail is at z=0..shaftLen.
        MeshData shaft = Primitives.CreateCylinder(shaftRadius, shaftLen, segments);
        var head = Primitives.CreateCone(headRadius, headLength, segments);   // spans -h/2..+h/2

        var merged = new MeshData();
        Append(merged, shaft, new Vector3(0f, 0f, shaftLen * 0.5f));
        Append(merged, head, new Vector3(0f, 0f, shaftLen + headLength * 0.5f));
        return merged;
    }

    /// <summary>Appends the src mesh to target after translating by offset (indices are offset automatically).</summary>
    private static void Append(MeshData target, MeshData src, Vector3 offset)
    {
        uint baseIndex = (uint)target.VertexCount;
        for (int v = 0; v < src.VertexCount; v++)
        {
            target.AddVertex(
                src.Positions[v] + offset,
                src.Normals[v],
                src.Uvs[v],
                src.Tangents[v]);
        }

        for (int i = 0; i < src.Indices.Count; i += 3)
        {
            target.AddTriangle(
                baseIndex + src.Indices[i],
                baseIndex + src.Indices[i + 1],
                baseIndex + src.Indices[i + 2]);
        }
    }
}
