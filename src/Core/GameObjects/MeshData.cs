using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.GameObjects;

/// <summary>
/// CPU 侧网格数据：顶点属性按 位置/法线/UV/切线 并行存储，外加索引缓冲。
/// 与 GPU/OpenGL 完全解耦；需要渲染时通过 <see cref="ToInterleavedArray"/>
/// 转换为渲染后端（RobotSimulation.OpenGL.Mesh）所需的交错 float[]（pos3|uv2|normal3|tangent3）。
/// </summary>
public sealed class MeshData
{
    private readonly List<Vector3> _positions = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Vector2> _uvs = new();
    private readonly List<Vector3> _tangents = new();
    private readonly List<uint> _indices = new();

    public int VertexCount => _positions.Count;

    /// <summary>三角形数量（索引数 / 3）。</summary>
    public int TriangleCount => _indices.Count / 3;

    public IReadOnlyList<Vector3> Positions => _positions;
    public IReadOnlyList<Vector3> Normals => _normals;
    public IReadOnlyList<Vector2> Uvs => _uvs;
    public IReadOnlyList<Vector3> Tangents => _tangents;
    public IReadOnlyList<uint> Indices => _indices;

    /// <summary>
    /// 添加一个顶点并返回其索引。
    /// </summary>
    public uint AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Vector3 tangent)
    {
        uint index = (uint)_positions.Count;
        _positions.Add(position);
        _normals.Add(normal);
        _uvs.Add(uv);
        _tangents.Add(tangent);
        return index;
    }

    /// <summary>
    /// 添加一个三角形（调用方需保证绕序为从外侧看 CCW）。
    /// </summary>
    public void AddTriangle(uint a, uint b, uint c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>
    /// 导出为渲染后端所需的交错顶点数组。
    /// 布局由 <see cref="VertexLayout"/> 定义（pos3 | uv2 | normal3 | tangent3）。
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

    /// <summary>
    /// 返回索引缓冲副本。
    /// </summary>
    public uint[] ToIndexArray() => _indices.ToArray();
}
