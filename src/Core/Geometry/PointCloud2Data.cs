using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Point cloud in ROS <c>sensor_msgs/PointCloud2</c> style: one flat byte buffer plus a
/// field description. Every point occupies exactly <see cref="PointStep"/> bytes, and each
/// channel (<c>x</c>, <c>y</c>, <c>z</c>, <c>rgb</c>, <c>intensity</c>, ...) is located by a
/// <see cref="PointField"/> (offset + data type + count). This lets the CPU hold organized or
/// unordered clouds of arbitrary channel layout without extra per-point classes.
///
/// Position (x/y/z) is required. Color is optional and resolved, in priority order, from a
/// packed <c>rgb</c>/<c>rgba</c> channel, separate <c>r</c>/<c>g</c>/<c>b</c> channels, or an
/// <c>intensity</c> channel (mapped to grayscale). When no color channel exists,
/// <see cref="ToColorArray"/> returns <c>null</c> and the renderer falls back to the
/// material's uniform color.
/// </summary>
public sealed class PointCloud2Data
{
    private readonly PointField[] _fields;
    private readonly byte[] _data;
    private readonly int _pointStep;

    // Resolved channels (validated once during construction).
    private PointField? _xField, _yField, _zField;
    private PointField? _rgbField;
    private PointField? _rField, _gField, _bField;
    private PointField? _intensityField;

    // Lazily computed intensity min/max for grayscale mapping.
    private float _intensityMin, _intensityMax;
    private bool _intensityRangeValid;

    public PointCloud2Data(PointField[] fields, byte[] data, int pointStep,
        string? frameId = null, int width = 0, int height = 1)
    {
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (pointStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(pointStep));
        if (_data.Length % pointStep != 0)
            throw new ArgumentException("Data length is not a multiple of pointStep.", nameof(data));

        _pointStep = pointStep;
        FrameId = frameId;
        Width = width <= 0 ? _data.Length / pointStep : width;
        Height = height;
        ResolveChannels(fields);
    }

    /// <summary>Optional source / coordinate frame identifier.</summary>
    public string? FrameId { get; }

    /// <summary>All declared fields, in declaration order.</summary>
    public IReadOnlyList<PointField> Fields => _fields;

    /// <summary>Bytes occupied by a single point.</summary>
    public int PointStep => _pointStep;

    /// <summary>Number of points per row (used when <see cref="IsOrganized"/>)..</summary>
    public int Width { get; }

    /// <summary>Number of rows; <c>1</c> means an unstructured point cloud.</summary>
    public int Height { get; }

    /// <summary>Total number of points.</summary>
    public int Count => _pointStep > 0 ? _data.Length / _pointStep : 0;

    /// <summary>Whether the cloud is organized as a 2D height map (height &gt; 1).</summary>
    public bool IsOrganized => Height > 1;

    /// <summary>True when the cloud carries a packed rgb, separate r/g/b, or intensity channel.</summary>
    public bool HasColor => _rgbField != null
        || (_rField != null && _gField != null && _bField != null)
        || _intensityField != null;

    /// <summary>Finds a field by any of the given names (case-insensitive). Returns null if absent.</summary>
    public PointField? FindField(params string[] names)
    {
        foreach (PointField f in _fields)
        {
            foreach (string n in names)
            {
                if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        return null;
    }

    private void ResolveChannels(PointField[] fields)
    {
        _xField = FindField("x");
        _yField = FindField("y");
        _zField = FindField("z");

        _rgbField = FindField("rgb", "rgba");
        _rField = FindField("red", "r");
        _gField = FindField("green", "g");
        _bField = FindField("blue", "b");
        _intensityField = FindField("intensity", "i");
    }

    /// <summary>Reads one float value of the given type from the point buffer.</summary>
    private float ReadFloat(ReadOnlySpan<byte> span, PointFieldDataType type) => type switch
    {
        PointFieldDataType.Float32 => BinaryPrimitives.ReadSingleLittleEndian(span),
        PointFieldDataType.Float64 => (float)BinaryPrimitives.ReadDoubleLittleEndian(span),
        PointFieldDataType.Int8 => (sbyte)span[0],
        PointFieldDataType.UInt8 => span[0],
        PointFieldDataType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
        PointFieldDataType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
        PointFieldDataType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
        PointFieldDataType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private float ReadFieldFloat(int pointIndex, PointField field)
    {
        int offset = pointIndex * _pointStep + field.Offset;
        return ReadFloat(_data.AsSpan(offset, PointField.GetElementSize(field.DataType)), field.DataType);
    }

    /// <summary>Gets the position of point <paramref name="index"/>.</summary>
    public Vector3 GetPosition(int index)
    {
        CheckIndex(index);
        if (_xField is null || _yField is null || _zField is null)
            throw new InvalidOperationException("Point cloud has no x/y/z position fields.");

        return new Vector3(
            ReadFieldFloat(index, _xField),
            ReadFieldFloat(index, _yField),
            ReadFieldFloat(index, _zField));
    }

    /// <summary>
    /// Gets the color of point <paramref name="index"/> as normalized RGBA (channels in 0..1).
    /// Throws if the cloud has no color channel.
    /// </summary>
    public Vector4 GetColor(int index)
    {
        CheckIndex(index);
        if (_rgbField != null)
            return DecodePackedRgb(index, _rgbField);
        if (_rField != null && _gField != null && _bField != null)
            return new Vector4(
                NormalizeColor(ReadFieldFloat(index, _rField), _rField.DataType),
                NormalizeColor(ReadFieldFloat(index, _gField), _gField.DataType),
                NormalizeColor(ReadFieldFloat(index, _bField), _bField.DataType),
                1f);
        if (_intensityField != null)
        {
            float t = MapIntensity(ReadFieldFloat(index, _intensityField));
            return new Vector4(t, t, t, 1f);
        }
        throw new InvalidOperationException("Point cloud has no color channel.");
    }

    private Vector4 DecodePackedRgb(int index, PointField field)
    {
        int offset = index * _pointStep + field.Offset;
        ReadOnlySpan<byte> span = _data.AsSpan(offset, 4);

        uint packed = field.DataType == PointFieldDataType.Float32
            ? BitConverter.SingleToUInt32Bits(BinaryPrimitives.ReadSingleLittleEndian(span))
            : BinaryPrimitives.ReadUInt32LittleEndian(span);

        // Typical RGB-8 packed layout stored as 0x00RRGGBB (big-endian byte order).
        return new Vector4(
            ((packed >> 16) & 0xFF) / 255f,
            ((packed >> 8) & 0xFF) / 255f,
            (packed & 0xFF) / 255f,
            1f);
    }


    /// <summary>Normalizes an individual color value from raw storage to 0..1.</summary>
    private static float NormalizeColor(float value, PointFieldDataType type) => type switch
    {
        PointFieldDataType.Float32 or PointFieldDataType.Float64 => Math.Clamp(value, 0f, 1f),
        PointFieldDataType.UInt8 => value / 255f,
        PointFieldDataType.Int8 => Math.Clamp(value / 255f, 0f, 1f),
        PointFieldDataType.UInt16 => value / 65535f,
        PointFieldDataType.Int16 => Math.Clamp(value / 32767f, 0f, 1f),
        PointFieldDataType.UInt32 => value / (float)uint.MaxValue,
        PointFieldDataType.Int32 => Math.Clamp(value / (float)int.MaxValue, 0f, 1f),
        _ => Math.Clamp(value, 0f, 1f),
    };

    private float MapIntensity(float value)
    {
        EnsureIntensityRange();
        float range = _intensityMax - _intensityMin;
        if (range <= 1e-6f)
            return 0.5f;
        return Math.Clamp((value - _intensityMin) / range, 0f, 1f);
    }

    private void EnsureIntensityRange()
    {
        if (_intensityRangeValid || _intensityField is null)
            return;

        _intensityMin = float.MaxValue;
        _intensityMax = float.MinValue;
        for (int i = 0; i < Count; i++)
        {
            float v = ReadFieldFloat(i, _intensityField);
            if (v < _intensityMin) _intensityMin = v;
            if (v > _intensityMax) _intensityMax = v;
        }
        _intensityRangeValid = true;
    }

    /// <summary>Exports all positions as a contiguous xyz float array (one float per component).</summary>
    public float[] ToPositionArray()
    {
        var result = new float[Count * 3];
        int i = 0;
        for (int p = 0; p < Count; p++)
        {
            Vector3 pos = GetPosition(p);
            result[i++] = pos.X;
            result[i++] = pos.Y;
            result[i++] = pos.Z;
        }
        return result;
    }

    /// <summary>
    /// Exports all colors as a contiguous rgba float array, or <c>null</c> if the cloud has
    /// no color channel (caller should then use the material uniform color).
    /// </summary>
    public float[]? ToColorArray()
    {
        if (!HasColor)
            return null;
        var result = new float[Count * 4];
        int i = 0;
        for (int p = 0; p < Count; p++)
        {
            Vector4 c = GetColor(p);
            result[i++] = c.X;
            result[i++] = c.Y;
            result[i++] = c.Z;
            result[i++] = c.W;
        }
        return result;
    }

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Creates a simple unstructured cloud holding only x/y/z float positions
    /// (no color channel). A convenience for simple point sets.
    /// </summary>
    public static PointCloud2Data FromPositions(IEnumerable<Vector3> positions, string? frameId = null)
    {
        if (positions is null)
            throw new ArgumentNullException(nameof(positions));

        List<Vector3> list = positions as List<Vector3> ?? new List<Vector3>(positions);
        var data = new byte[list.Count * 12];
        int off = 0;
        foreach (Vector3 p in list)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off), p.X);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off + 4), p.Y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(off + 8), p.Z);
            off += 12;
        }

        return new PointCloud2Data(
            new[]
            {
                new PointField("x", 0, PointFieldDataType.Float32),
                new PointField("y", 4, PointFieldDataType.Float32),
                new PointField("z", 8, PointFieldDataType.Float32),
            },
            data,
            12,
            frameId,
            width: list.Count,
            height: 1);
    }
}
