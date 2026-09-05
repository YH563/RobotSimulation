using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// CPU 侧线段集（每两个顶点 = 一条线段，对应 GL_LINES 语义）。
/// 可选"逐顶点颜色"（用于坐标系等一物多色的线）；若不使用颜色，
/// 由挂载对象 <see cref="RobotSimulation.Core.Rendering.MaterialData.BaseColor"/> 统一上色。
/// </summary>
public sealed class LineData
{
    private readonly List<Vector3> _positions = new();
    private List<Vector4>? _colors;   // 惰性创建；一旦使用逐顶点颜色，后续所有线段都需补色

    /// <summary>顶点总数（= 线段数 × 2）。</summary>
    public int VertexCount => _positions.Count;

    /// <summary>线段数量。</summary>
    public int SegmentCount => _positions.Count / 2;

    public IReadOnlyList<Vector3> Positions => _positions;

    /// <summary>是否启用逐顶点颜色（至少一条线段带颜色后为 true）。</summary>
    public bool HasPerVertexColors => _colors != null;

    /// <summary>逐顶点颜色（与 Positions 等长；未带色的线段补不透明白）。</summary>
    public IReadOnlyList<Vector4>? Colors => _colors;

    /// <summary>追加一条线段（使用材质色）。若已启用逐顶点颜色，则以白色补齐。</summary>
    public void AddSegment(Vector3 a, Vector3 b)
        => AddSegmentInternal(a, b, _colors == null ? null : Vector4.One);

    /// <summary>追加一条逐顶点同色的线段（会启用逐顶点颜色模式）。</summary>
    public void AddSegment(Vector3 a, Vector3 b, Vector4 color)
        => AddSegmentInternal(a, b, color);

    private void AddSegmentInternal(Vector3 a, Vector3 b, Vector4? color)
    {
        _positions.Add(a);
        _positions.Add(b);

        if (_colors is not null)
        {
            _colors.Add(color!.Value);
            _colors.Add(color!.Value);
        }
        else if (color.HasValue)
        {
            _colors = new List<Vector4>(_positions.Count);
            for (int i = 0; i < _positions.Count; i++)
                _colors.Add(Vector4.One);
            _colors[^2] = color.Value;
            _colors[^1] = color.Value;
        }
    }

    /// <summary>导出连续顶点数组（每段两个顶点），供渲染后端上传 GL_LINES。</summary>
    public float[] ToPositionArray()
    {
        var result = new float[_positions.Count * 3];
        int i = 0;
        foreach (Vector3 p in _positions)
        {
            result[i++] = p.X;
            result[i++] = p.Y;
            result[i++] = p.Z;
        }
        return result;
    }

    /// <summary>导出逐顶点颜色数组（仅 <see cref="HasPerVertexColors"/> 时有意义）。</summary>
    public float[]? ToColorArray()
    {
        if (_colors is null)
            return null;

        var result = new float[_colors.Count * 4];
        int i = 0;
        foreach (Vector4 c in _colors)
        {
            result[i++] = c.X;
            result[i++] = c.Y;
            result[i++] = c.Z;
            result[i++] = c.W;
        }
        return result;
    }
}

