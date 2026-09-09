using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// CPU-side line set (every two vertices form one segment, matching GL_LINES semantics).
/// An optional "per-vertex color" (used for multi-colored lines such as axes); when colors are
/// not used, the owning object's <see cref="RobotSimulation.Core.Rendering.MaterialData.BaseColor"/>
/// tints the whole set.
/// </summary>
public sealed class LineData
{
    private readonly List<Vector3> _positions = new();
    private List<Vector4>? _colors;   // Lazily created; once per-vertex color is used, every later segment must supply a color.

    /// <summary>Total vertex count (= segment count × 2).</summary>
    public int VertexCount => _positions.Count;

    /// <summary>Number of segments.</summary>
    public int SegmentCount => _positions.Count / 2;

    public IReadOnlyList<Vector3> Positions => _positions;

    /// <summary>Whether per-vertex coloring is enabled (true once at least one segment carries a color).</summary>
    public bool HasPerVertexColors => _colors != null;

    /// <summary>Per-vertex colors (same length as <see cref="Positions"/>; uncolored segments are padded with opaque white).</summary>
    public IReadOnlyList<Vector4>? Colors => _colors;

    /// <summary>Appends a segment using the material color. If per-vertex color is already enabled, pads with white.</summary>
    public void AddSegment(Vector3 a, Vector3 b)
        => AddSegmentInternal(a, b, _colors == null ? null : Vector4.One);

    /// <summary>Appends a per-vertex colored segment (also enables per-vertex color mode).</summary>
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

    /// <summary>Exports a contiguous vertex array (two per segment) for uploading GL_LINES.</summary>
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

    /// <summary>Exports the per-vertex color array (only meaningful when <see cref="HasPerVertexColors"/>).</summary>
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
