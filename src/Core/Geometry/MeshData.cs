using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// CPU-side mesh data: vertex attributes stored in parallel position/normal/UV/tangent arrays,
/// plus an index buffer. Fully decoupled from GPU/OpenGL; when rendering is needed, convert via
/// <see cref="ToInterleavedArray"/> into the interleaved float[] (pos3|uv2|normal3|tangent3)
/// that the rendering backend (RobotSimulation.OpenGL.Mesh) expects.
/// </summary>
public sealed class MeshData
{
    private readonly List<Vector3> _positions = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Vector2> _uvs = new();
    private readonly List<Vector3> _tangents = new();
    private readonly List<uint> _indices = new();

    public int VertexCount => _positions.Count;

    /// <summary>Number of triangles (index count / 3).</summary>
    public int TriangleCount => _indices.Count / 3;

    public IReadOnlyList<Vector3> Positions => _positions;
    public IReadOnlyList<Vector3> Normals => _normals;
    public IReadOnlyList<Vector2> Uvs => _uvs;
    public IReadOnlyList<Vector3> Tangents => _tangents;
    public IReadOnlyList<uint> Indices => _indices;

    /// <summary>Adds a vertex and returns its index.</summary>
    public uint AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 tangent)
    {
        uint index = (uint)_positions.Count;
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        _tangents.Add(tangent);
        return index;
    }

    /// <summary>Adds a triangle (the caller guarantees CCW winding viewed from outside).</summary>
    public void AddTriangle(uint a, uint b, uint c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>
    /// Exports the interleaved vertex array expected by the rendering backend.
    /// The layout is defined by <see cref="VertexLayout"/> (pos3 | uv2 | normal3 | tangent3).
    /// </summary>
    public float[] ToInterleavedArray()
    {
        var result = new float[VertexCount * VertexLayout.FloatsPerVertex];
        int i = 0;
        for (int v = 0; v < VertexCount; v++)
        {
            result[i++] = _positions[v].X;
            result[i++] = _positions[v].Y;
            result[i++] = _positions[v].Z;

            result[i++] = _uvs[v].X;
            result[i++] = _uvs[v].Y;

            result[i++] = _normals[v].X;
            result[i++] = _normals[v].Y;
            result[i++] = _normals[v].Z;

            result[i++] = _tangents[v].X;
            result[i++] = _tangents[v].Y;
            result[i++] = _tangents[v].Z;
        }
        return result;
    }

    /// <summary>Returns a copy of the index buffer.</summary>
    public uint[] ToIndexArray() => _indices.ToArray();

    /// <summary>
    /// Computes the local-space axis-aligned bounding box, for use with picking broad-phase.
    /// An empty mesh returns an empty box (Min = Max = 0). This is a CPU O(n) computation;
    /// for frequently used custom meshes the caller should cache the result (this class does not
    /// cache, keeping MeshData reusable and shareable across threads).
    /// </summary>
    public Bounds ComputeBounds()
    {
        if (_positions.Count == 0)
            return new Bounds(Vector3.Zero, Vector3.Zero);

        Vector3 min = _positions[0], max = _positions[0];
        for (int i = 1; i < _positions.Count; i++)
        {
            Vector3 p = _positions[i];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new Bounds(min, max);
    }
}
