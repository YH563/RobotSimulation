using System;
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
    private long _revision;

    /// <summary>Number of vertices (every parallel attribute array below has exactly this length).</summary>
    public int VertexCount => _positions.Count;

    /// <summary>Number of triangles (index count / 3).</summary>
    public int TriangleCount => _indices.Count / 3;

    /// <summary>Whether the mesh carries no geometry at all.</summary>
    public bool IsEmpty => _positions.Count == 0 && _indices.Count == 0;

    /// <summary>
    /// Monotonic content revision, incremented by every mutation (vertex/triangle append, whole-geometry
    /// replacement, clear). A render backend compares it with the revision it last uploaded to decide whether
    /// it has to touch the GPU buffers at all — the mesh-level counterpart of <see cref="PointCloud2Data.Revision"/>.
    /// </summary>
    public long Revision => _revision;

    /// <summary>Vertex positions, in local model space.</summary>
    public IReadOnlyList<Vector3> Positions => _positions;

    /// <summary>Vertex normals (unit length; used for lighting).</summary>
    public IReadOnlyList<Vector3> Normals => _normals;

    /// <summary>Texture coordinates, one per vertex.</summary>
    public IReadOnlyList<Vector2> Uvs => _uvs;

    /// <summary>Vertex tangents (used with the normals for normal mapping).</summary>
    public IReadOnlyList<Vector3> Tangents => _tangents;

    /// <summary>Triangle indices, three consecutive entries per triangle.</summary>
    public IReadOnlyList<uint> Indices => _indices;

    /// <summary>Adds a vertex and returns its index.</summary>
    public uint AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 tangent)
    {
        uint index = (uint)_positions.Count;
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        _tangents.Add(tangent);
        _revision++;
        return index;
    }

    /// <summary>Adds a triangle (the caller guarantees CCW winding viewed from outside).</summary>
    public void AddTriangle(uint a, uint b, uint c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
        _revision++;
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
    /// Replaces the whole geometry with vertices laid out in the <see cref="VertexLayout"/> interleaved
    /// format (pos3 | uv2 | normal3 | tangent3) and a triangle-list index buffer. This is the bulk entry
    /// point an external producer (a reconstruction backend) uses: it is the inverse of
    /// <see cref="ToInterleavedArray"/> and the mesh counterpart of "replace the whole chunk".
    /// </summary>
    /// <param name="interleavedVertices">Vertices, <see cref="VertexLayout.FloatsPerVertex"/> floats each.</param>
    /// <param name="indices">Triangle indices, three per triangle, each below the vertex count.</param>
    /// <exception cref="ArgumentException">The vertex array is not a multiple of the vertex stride, the index count is not a multiple of 3, or an index is out of range.</exception>
    public void SetGeometry(ReadOnlySpan<float> interleavedVertices, ReadOnlySpan<uint> indices)
    {
        if (interleavedVertices.Length % VertexLayout.FloatsPerVertex != 0)
            throw new ArgumentException(
                $"Vertex array length must be a multiple of {VertexLayout.FloatsPerVertex} (FloatsPerVertex).",
                nameof(interleavedVertices));
        if (indices.Length % 3 != 0)
            throw new ArgumentException("Index count must be a multiple of 3 (a triangle list).", nameof(indices));

        int vertexCount = interleavedVertices.Length / VertexLayout.FloatsPerVertex;

        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= vertexCount)
                throw new ArgumentException(
                    $"Index {indices[i]} at position {i} is outside the {vertexCount} vertex/vertices.",
                    nameof(indices));
        }

        _positions.Clear();
        _normals.Clear();
        _uvs.Clear();
        _tangents.Clear();
        _indices.Clear();

        _positions.EnsureCapacity(vertexCount);
        _normals.EnsureCapacity(vertexCount);
        _uvs.EnsureCapacity(vertexCount);
        _tangents.EnsureCapacity(vertexCount);
        _indices.EnsureCapacity(indices.Length);

        for (int i = 0; i < vertexCount; i++)
        {
            int b = i * VertexLayout.FloatsPerVertex;
            _positions.Add(new Vector3(interleavedVertices[b], interleavedVertices[b + 1], interleavedVertices[b + 2]));
            _uvs.Add(new Vector2(interleavedVertices[b + 3], interleavedVertices[b + 4]));
            _normals.Add(new Vector3(interleavedVertices[b + 5], interleavedVertices[b + 6], interleavedVertices[b + 7]));
            _tangents.Add(new Vector3(interleavedVertices[b + 8], interleavedVertices[b + 9], interleavedVertices[b + 10]));
        }

        for (int i = 0; i < indices.Length; i++)
            _indices.Add(indices[i]);

        _revision++;
    }

    /// <summary>Drops the whole geometry (vertices and indices); a no-op when already empty.</summary>
    public void Clear()
    {
        if (IsEmpty)
            return;

        _positions.Clear();
        _normals.Clear();
        _uvs.Clear();
        _tangents.Clear();
        _indices.Clear();
        _revision++;
    }

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
