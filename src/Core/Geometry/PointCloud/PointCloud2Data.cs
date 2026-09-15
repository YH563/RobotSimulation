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
///
/// A cloud can also grow. Points are appended at the write cursor and the oldest ones are dropped by
/// moving the ring start (<see cref="FirstSlot"/>), so a stream of scans can be kept as a sliding
/// window without ever copying point data or re-uploading points that are already on the GPU. What
/// changed since the last upload is reported through <see cref="Revision"/> and <see cref="Dirty"/>.
/// </summary>
public sealed class PointCloud2Data
{
    /// <summary>Slot count of a mutable cloud's first growth step.</summary>
    private const int MinCapacity = 16;

    private readonly PointField[] _fields;
    private readonly int _pointStep;

    // Backing store: Capacity slots of PointStep bytes each. Only growth replaces the array.
    private byte[] _data;

    private int _capacity;   // Slots in _data (= _data.Length / _pointStep).
    private int _count;      // Valid points, always <= _capacity.
    private int _firstSlot;  // Slot holding logical point 0 (the ring start).

    // Width a cloud was built with; an unstructured cloud derives it from Count instead.
    private readonly int _explicitWidth;

    // Revision + accumulated change window (what a backend needs for a partial upload).
    // FromRevision is only advanced by ResetDirty: it is "the revision a backend is in sync with",
    // so mutations that move no data (eviction) or drop everything (clear) must not touch it.
    private long _revision;
    private long _dirtyFromRevision;
    private int _dirtyStart;   // Logical index, inclusive.
    private int _dirtyEnd;     // Logical index, exclusive.

    // Resolved channels (validated once during construction).
    private PointField? _xField, _yField, _zField;
    private PointField? _rgbField;
    private PointField? _rField, _gField, _bField;
    private PointField? _intensityField;

    // Lazily computed intensity min/max for grayscale mapping.
    private float _intensityMin, _intensityMax;
    private bool _intensityRangeValid;

    /// <summary>
    /// Wraps an already-decoded, layout-described point cloud (the in-memory form of a
    /// <c>sensor_msgs/PointCloud2</c> message). Channels referenced by name (x/y/z, rgb or
    /// red/green/blue, intensity) are resolved and validated once, here.
    /// </summary>
    /// <param name="fields">Channel descriptions, in declaration order.</param>
    /// <param name="data">Raw point bytes (one point per <paramref name="pointStep"/> bytes).</param>
    /// <param name="pointStep">Bytes per point.</param>
    /// <param name="frameId">Optional source/coordinate frame identifier.</param>
    /// <param name="width">Number of points per row; 0 derives it from the byte length.</param>
    /// <param name="height">Number of rows; 1 means an unstructured cloud.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/>'s length is not a multiple of <paramref name="pointStep"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pointStep"/> is not positive.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> or <paramref name="data"/> is null.</exception>
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
        _capacity = _data.Length / pointStep;
        _count = _capacity;
        _explicitWidth = width > 0 ? width : _count;
        FrameId = frameId;
        Height = height;
        ResolveChannels(fields);
    }

    /// <summary>Optional source / coordinate frame identifier.</summary>
    public string? FrameId { get; }

    /// <summary>All declared fields, in declaration order.</summary>
    public IReadOnlyList<PointField> Fields => _fields;

    /// <summary>Bytes occupied by a single point.</summary>
    public int PointStep => _pointStep;

    /// <summary>
    /// Number of points per row. An unstructured cloud (height 1) always reports
    /// <see cref="Count"/>, so appending points keeps it consistent; an organized cloud keeps the
    /// width it was built with.
    /// </summary>
    public int Width => IsOrganized ? _explicitWidth : _count;

    /// <summary>Number of rows; <c>1</c> means an unstructured point cloud.</summary>
    public int Height { get; }

    /// <summary>Number of valid points (never above <see cref="Capacity"/>; a whole buffer starts out full).</summary>
    public int Count => _count;

    /// <summary>Number of point slots the backing store can hold before it must grow.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Physical slot holding logical point 0, i.e. the oldest point. It is 0 until
    /// <see cref="RemoveOldest"/> is used: eviction moves this cursor instead of shifting the points.
    /// </summary>
    public int FirstSlot => _firstSlot;

    /// <summary>Whether the cloud is organized as a 2D height map (height &gt; 1).</summary>
    public bool IsOrganized => Height > 1;

    /// <summary>
    /// Incremented by every content change (append, overwrite, evict, clear, grow). A render backend
    /// compares it with the revision it last uploaded to decide whether it has to do anything at all.
    /// </summary>
    public long Revision => _revision;

    /// <summary>
    /// Accumulated change window since the last <see cref="ResetDirty"/>: the logical points in
    /// [Start, Start + Count) changed. A backend that has already uploaded up to
    /// <see cref="DirtyWindow.FromRevision"/> may upload just that range; any other backend must
    /// upload everything. Scattering <see cref="SetPoint"/> calls across the cloud widens the window
    /// (it is one contiguous range, not a set of ranges).
    /// </summary>
    public DirtyWindow Dirty => new(_dirtyFromRevision, _revision, _dirtyStart, _dirtyEnd - _dirtyStart);

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

    /// <summary>Maps a logical point index (0 = oldest) onto its physical slot inside the ring.</summary>
    private int SlotOf(int logicalIndex) => _capacity == 0 ? 0 : (_firstSlot + logicalIndex) % _capacity;

    private float ReadSlotFloat(int slot, PointField field)
    {
        int offset = slot * _pointStep + field.Offset;
        return ReadFloat(_data.AsSpan(offset, PointField.GetElementSize(field.DataType)), field.DataType);
    }

    /// <summary>Writes one float value of the given type into the point buffer (integers are rounded and clamped).</summary>
    private static void WriteFloat(Span<byte> span, PointFieldDataType type, float value)
    {
        switch (type)
        {
            case PointFieldDataType.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(span, value);
                break;
            case PointFieldDataType.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(span, value);
                break;
            case PointFieldDataType.Int8:
                span[0] = (byte)(sbyte)Math.Clamp(Math.Round(value), sbyte.MinValue, sbyte.MaxValue);
                break;
            case PointFieldDataType.UInt8:
                span[0] = (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue);
                break;
            case PointFieldDataType.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(span,
                    (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue));
                break;
            case PointFieldDataType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(span,
                    (ushort)Math.Clamp(Math.Round(value), ushort.MinValue, ushort.MaxValue));
                break;
            case PointFieldDataType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(span,
                    (int)Math.Clamp(Math.Round((double)value), int.MinValue, int.MaxValue));
                break;
            case PointFieldDataType.UInt32:
                BinaryPrimitives.WriteUInt32LittleEndian(span,
                    (uint)Math.Clamp(Math.Round((double)value), uint.MinValue, uint.MaxValue));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    private void WriteSlotFloat(int slot, PointField field, float value)
    {
        int offset = slot * _pointStep + field.Offset;
        WriteFloat(_data.AsSpan(offset, PointField.GetElementSize(field.DataType)), field.DataType, value);
    }

    /// <summary>
    /// Zero-fills one slot. A newly appended point goes into a slot that may still hold an evicted
    /// point, so the channels the caller does not write have to be cleared instead of inherited.
    /// </summary>
    private void ClearSlot(int slot) => _data.AsSpan(slot * _pointStep, _pointStep).Clear();

    private void WritePosition(int slot, Vector3 position)
    {
        if (_xField is null || _yField is null || _zField is null)
            throw new InvalidOperationException("Point cloud has no x/y/z position fields.");

        WriteSlotFloat(slot, _xField, position.X);
        WriteSlotFloat(slot, _yField, position.Y);
        WriteSlotFloat(slot, _zField, position.Z);
    }

    /// <summary>Writes a normalized RGBA color into whichever color channel the layout has.</summary>
    private void WriteColor(int slot, Vector4 color)
    {
        if (_rgbField != null)
        {
            WritePackedRgb(slot, _rgbField, color);
        }
        else if (_rField != null && _gField != null && _bField != null)
        {
            WriteSlotFloat(slot, _rField, DenormalizeColor(color.X, _rField.DataType));
            WriteSlotFloat(slot, _gField, DenormalizeColor(color.Y, _gField.DataType));
            WriteSlotFloat(slot, _bField, DenormalizeColor(color.Z, _bField.DataType));
        }
        else if (_intensityField != null)
        {
            // An intensity-only cloud has no color to write, so the channel gets the luminance.
            WriteSlotFloat(slot, _intensityField, Math.Clamp((color.X + color.Y + color.Z) / 3f, 0f, 1f));
        }
        else
        {
            throw new InvalidOperationException("Point cloud has no color channel.");
        }
    }

    private void WritePackedRgb(int slot, PointField field, Vector4 color)
    {
        uint packed = (PackChannel(color.X) << 16) | (PackChannel(color.Y) << 8) | PackChannel(color.Z);
        Span<byte> span = _data.AsSpan(slot * _pointStep + field.Offset, 4);

        if (field.DataType == PointFieldDataType.Float32)
            BinaryPrimitives.WriteSingleLittleEndian(span, BitConverter.UInt32BitsToSingle(packed));
        else if (field.DataType == PointFieldDataType.UInt32)
            BinaryPrimitives.WriteUInt32LittleEndian(span, packed);
        else
            throw new InvalidOperationException($"Packed rgb field must be float32 or uint32, but is {field.DataType}.");
    }

    private static uint PackChannel(float value) => (uint)Math.Clamp(value * 255f + 0.5f, 0f, 255f);

    /// <summary>Maps a 0..1 color component back onto the field's raw storage (inverse of <see cref="NormalizeColor"/>).</summary>
    private static float DenormalizeColor(float value, PointFieldDataType type) => type switch
    {
        PointFieldDataType.UInt8 or PointFieldDataType.Int8 => Math.Clamp(value, 0f, 1f) * 255f,
        PointFieldDataType.UInt16 => Math.Clamp(value, 0f, 1f) * 65535f,
        PointFieldDataType.Int16 => Math.Clamp(value, 0f, 1f) * 32767f,
        PointFieldDataType.UInt32 => Math.Clamp(value, 0f, 1f) * uint.MaxValue,
        PointFieldDataType.Int32 => Math.Clamp(value, 0f, 1f) * int.MaxValue,
        _ => Math.Clamp(value, 0f, 1f),
    };

    /// <summary>
    /// Drops the cached intensity range after a write. The range is recomputed on the next color read
    /// (an O(Count) scan), which is the price of mapping intensity to greyscale; clouds with a real
    /// color channel never pay it.
    /// </summary>
    private void InvalidateIntensityRange()
    {
        if (_intensityField != null)
            _intensityRangeValid = false;
    }

    /// <summary>Gets the position of point <paramref name="index"/> (0 = oldest).</summary>
    public Vector3 GetPosition(int index)
    {
        CheckIndex(index);
        if (_xField is null || _yField is null || _zField is null)
            throw new InvalidOperationException("Point cloud has no x/y/z position fields.");

        int slot = SlotOf(index);
        return new Vector3(
            ReadSlotFloat(slot, _xField),
            ReadSlotFloat(slot, _yField),
            ReadSlotFloat(slot, _zField));
    }

    /// <summary>
    /// Gets the color of point <paramref name="index"/> as normalized RGBA (channels in 0..1).
    /// Throws if the cloud has no color channel.
    /// </summary>
    public Vector4 GetColor(int index)
    {
        CheckIndex(index);
        return ReadSlotColor(SlotOf(index));
    }

    /// <summary>Reads the color stored in one physical slot.</summary>
    private Vector4 ReadSlotColor(int slot)
    {
        if (_rgbField != null)
            return DecodePackedRgb(slot, _rgbField);
        if (_rField != null && _gField != null && _bField != null)
            return new Vector4(
                NormalizeColor(ReadSlotFloat(slot, _rField), _rField.DataType),
                NormalizeColor(ReadSlotFloat(slot, _gField), _gField.DataType),
                NormalizeColor(ReadSlotFloat(slot, _bField), _bField.DataType),
                1f);
        if (_intensityField != null)
        {
            float t = MapIntensity(ReadSlotFloat(slot, _intensityField));
            return new Vector4(t, t, t, 1f);
        }
        throw new InvalidOperationException("Point cloud has no color channel.");
    }

    private Vector4 DecodePackedRgb(int slot, PointField field)
    {
        int offset = slot * _pointStep + field.Offset;
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
        for (int i = 0; i < _count; i++)
        {
            float v = ReadSlotFloat(SlotOf(i), _intensityField);
            if (v < _intensityMin) _intensityMin = v;
            if (v > _intensityMax) _intensityMax = v;
        }
        _intensityRangeValid = true;
    }

    /// <summary>Exports all positions as a contiguous xyz float array, oldest point first.</summary>
    public float[] ToPositionArray()
    {
        var result = new float[_count * 3];
        CopyPositionsTo(0, _count, result);
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
        var result = new float[_count * 4];
        CopyColorsTo(0, _count, result);
        return result;
    }

    /// <summary>Copies the positions of logical points [<paramref name="start"/>, start + count) as 3 floats per point.</summary>
    /// <param name="start">First logical index (the oldest point of the range).</param>
    /// <param name="count">Number of points.</param>
    /// <param name="destination">Receives <c>3 × count</c> floats.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside [0, Count).</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public void CopyPositionsTo(int start, int count, Span<float> destination)
    {
        ValidateRange(start, count);
        if (destination.Length < count * 3)
            throw new ArgumentException($"Destination needs {count * 3} floats.", nameof(destination));

        Span<SlotRun> runs = stackalloc SlotRun[2];
        int runCount = GetSlotRuns(start, count, runs);
        int written = 0;
        for (int i = 0; i < runCount; i++)
        {
            CopySlotPositions(runs[i].Slot, runs[i].Count, destination.Slice(written, runs[i].Count * 3));
            written += runs[i].Count * 3;
        }
    }

    /// <summary>Copies the colors of logical points [<paramref name="start"/>, start + count) as 4 floats per point.</summary>
    /// <param name="start">First logical index (the oldest point of the range).</param>
    /// <param name="count">Number of points.</param>
    /// <param name="destination">Receives <c>4 × count</c> floats.</param>
    /// <returns>False when the cloud has no color channel (nothing is written).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside [0, Count).</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public bool CopyColorsTo(int start, int count, Span<float> destination)
    {
        if (!HasColor)
            return false;
        ValidateRange(start, count);
        if (destination.Length < count * 4)
            throw new ArgumentException($"Destination needs {count * 4} floats.", nameof(destination));

        Span<SlotRun> runs = stackalloc SlotRun[2];
        int runCount = GetSlotRuns(start, count, runs);
        int written = 0;
        for (int i = 0; i < runCount; i++)
        {
            CopySlotColors(runs[i].Slot, runs[i].Count, destination.Slice(written, runs[i].Count * 4));
            written += runs[i].Count * 4;
        }
        return true;
    }

    /// <summary>
    /// Splits logical points [<paramref name="start"/>, start + count) into the physically contiguous
    /// slot runs holding them: one run, or two when the range wraps around the ring end. This is what
    /// lets a render backend update a vertex buffer range by range instead of re-uploading the cloud.
    /// </summary>
    /// <param name="start">First logical index.</param>
    /// <param name="count">Number of points (0 yields no runs).</param>
    /// <param name="runs">Receives the runs; must hold at least 2 elements.</param>
    /// <returns>Number of runs written (0, 1 or 2).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside [0, Count).</exception>
    /// <exception cref="ArgumentException"><paramref name="runs"/> holds fewer than 2 elements.</exception>
    public int GetSlotRuns(int start, int count, Span<SlotRun> runs)
    {
        ValidateRange(start, count);
        if (count == 0)
            return 0;
        if (runs.Length < 2)
            throw new ArgumentException("Two runs are needed: a range can wrap around the ring once.", nameof(runs));

        int slot = SlotOf(start);
        int first = Math.Min(count, _capacity - slot);
        runs[0] = new SlotRun(slot, first);
        if (first == count)
            return 1;

        runs[1] = new SlotRun(0, count - first);
        return 2;
    }

    /// <summary>Copies the positions of physical slots [<paramref name="slot"/>, slot + count) as 3 floats per point (backend use).</summary>
    /// <param name="slot">First physical slot.</param>
    /// <param name="count">Number of slots.</param>
    /// <param name="destination">Receives <c>3 × count</c> floats.</param>
    /// <exception cref="ArgumentOutOfRangeException">The slot range leaves the backing store.</exception>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z fields.</exception>
    public void CopySlotPositions(int slot, int count, Span<float> destination)
    {
        EnsureXyz();
        ValidateSlots(slot, count);
        if (destination.Length < count * 3)
            throw new ArgumentException($"Destination needs {count * 3} floats.", nameof(destination));

        for (int i = 0; i < count; i++)
        {
            int s = slot + i;
            destination[i * 3] = ReadSlotFloat(s, _xField!);
            destination[i * 3 + 1] = ReadSlotFloat(s, _yField!);
            destination[i * 3 + 2] = ReadSlotFloat(s, _zField!);
        }
    }

    /// <summary>Copies the colors of physical slots [<paramref name="slot"/>, slot + count) as 4 floats per point (backend use).</summary>
    /// <param name="slot">First physical slot.</param>
    /// <param name="count">Number of slots.</param>
    /// <param name="destination">Receives <c>4 × count</c> floats.</param>
    /// <returns>False when the cloud has no color channel (nothing is written).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The slot range leaves the backing store.</exception>
    public bool CopySlotColors(int slot, int count, Span<float> destination)
    {
        if (!HasColor)
            return false;
        ValidateSlots(slot, count);
        if (destination.Length < count * 4)
            throw new ArgumentException($"Destination needs {count * 4} floats.", nameof(destination));

        for (int i = 0; i < count; i++)
        {
            Vector4 c = ReadSlotColor(slot + i);
            destination[i * 4] = c.X;
            destination[i * 4 + 1] = c.Y;
            destination[i * 4 + 2] = c.Z;
            destination[i * 4 + 3] = c.W;
        }
        return true;
    }

    private void CheckIndex(int index)
    {
        if (index < 0 || index >= _count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    // ------------------------------------------------------------------
    // Incremental writes
    //
    // Logical index 0 is the oldest point and Count - 1 the newest; physical slots (what the render
    // backend uploads) follow from FirstSlot and Capacity. Appending writes at the write cursor and
    // eviction only advances FirstSlot, so points already on the GPU never move and a backend can
    // upload just the changed slots instead of the whole cloud.
    // ------------------------------------------------------------------

    /// <summary>
    /// Appends one point, writing only x/y/z: every other channel of the new point is zero. The
    /// backing store grows (capacity doubling) when it is full.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z fields, or is organized (height &gt; 1).</exception>
    public void AddPoint(Vector3 position)
    {
        Span<Vector3> single = stackalloc Vector3[1];
        single[0] = position;
        AddPoints(single);
    }

    /// <summary>Appends one point and writes <paramref name="color"/> into the cloud's color channel.</summary>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z fields, no color channel, or is organized.</exception>
    public void AddPoint(Vector3 position, Vector4 color)
    {
        Span<Vector3> single = stackalloc Vector3[1];
        Span<Vector4> singleColor = stackalloc Vector4[1];
        single[0] = position;
        singleColor[0] = color;
        AddPoints(single, singleColor);
    }

    /// <summary>Appends several points, writing only their positions (all other channels stay zero).</summary>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z fields, or is organized (height &gt; 1).</exception>
    public void AddPoints(ReadOnlySpan<Vector3> positions)
    {
        if (positions.Length == 0)
            return;

        EnsureXyz();
        PrepareAppend(positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            int slot = SlotOf(_count + i);
            ClearSlot(slot);
            WritePosition(slot, positions[i]);
        }
        CommitAppend(positions.Length);
    }

    /// <summary>Appends several points with one color per point.</summary>
    /// <exception cref="ArgumentException">The spans differ in length.</exception>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z or no color channel, or is organized.</exception>
    public void AddPoints(ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector4> colors)
    {
        if (colors.Length != positions.Length)
            throw new ArgumentException("One color per position is required.", nameof(colors));
        if (positions.Length == 0)
            return;

        EnsureXyz();
        if (!HasColor)
            throw new InvalidOperationException("Point cloud has no color channel.");

        PrepareAppend(positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            int slot = SlotOf(_count + i);
            ClearSlot(slot);
            WritePosition(slot, positions[i]);
            WriteColor(slot, colors[i]);
        }
        CommitAppend(positions.Length);
    }

    /// <summary>
    /// Appends one already-encoded point (exactly <see cref="PointStep"/> bytes, copied verbatim), for
    /// layouts whose channels the caller encodes itself.
    /// </summary>
    /// <exception cref="ArgumentException">The span is not exactly <see cref="PointStep"/> bytes long.</exception>
    /// <exception cref="InvalidOperationException">The cloud is organized (height &gt; 1).</exception>
    public void AppendRawPoint(ReadOnlySpan<byte> point)
    {
        if (point.Length != _pointStep)
            throw new ArgumentException($"A raw point must be exactly {_pointStep} bytes (PointStep).", nameof(point));

        PrepareAppend(1);
        point.CopyTo(_data.AsSpan(SlotOf(_count) * _pointStep, _pointStep));
        CommitAppend(1);
    }

    /// <summary>Overwrites the position of an existing point; its other channels are left untouched.</summary>
    /// <param name="index">Logical index (0 = oldest).</param>
    /// <param name="position">New position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    /// <exception cref="InvalidOperationException">The cloud has no x/y/z fields.</exception>
    public void SetPoint(int index, Vector3 position)
    {
        EnsureXyz();
        CheckIndex(index);
        WritePosition(SlotOf(index), position);
        TouchWindow(index, 1);
    }

    /// <summary>Overwrites the color of an existing point.</summary>
    /// <param name="index">Logical index (0 = oldest).</param>
    /// <param name="color">New color (RGBA, components in 0..1).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    /// <exception cref="InvalidOperationException">The cloud has no color channel.</exception>
    public void SetColor(int index, Vector4 color)
    {
        if (!HasColor)
            throw new InvalidOperationException("Point cloud has no color channel.");
        CheckIndex(index);
        WriteColor(SlotOf(index), color);
        TouchWindow(index, 1);
    }

    /// <summary>
    /// Drops the oldest <paramref name="count"/> points. This is O(1): the ring start moves instead of
    /// the points, so a sliding window costs neither a copy nor a re-upload.
    /// </summary>
    /// <param name="count">Number of oldest points to drop; clamped to <see cref="Count"/>.</param>
    public void RemoveOldest(int count)
    {
        if (count <= 0 || _count == 0)
            return;
        if (count >= _count)
        {
            Clear();
            return;
        }

        _firstSlot = SlotOf(count);
        _count -= count;
        ShiftDirtyWindow(count);      // The window is indexed logically, so dropping points moves it too.
        InvalidateIntensityRange();   // The dropped points may have supplied the min/max.
        _revision++;
    }

    /// <summary>Keeps only the newest <paramref name="maxPoints"/> points (the sliding window of a stream).</summary>
    /// <param name="maxPoints">Upper bound on <see cref="Count"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPoints"/> is negative.</exception>
    public void TrimTo(int maxPoints)
    {
        if (maxPoints < 0)
            throw new ArgumentOutOfRangeException(nameof(maxPoints));
        if (_count > maxPoints)
            RemoveOldest(_count - maxPoints);
    }

    /// <summary>
    /// Drops every point. The backing store and its capacity are kept for reuse, and an up-to-date
    /// backend only has to stop drawing: there is nothing to upload.
    /// </summary>
    public void Clear()
    {
        if (_count == 0 && _firstSlot == 0)
            return;

        _count = 0;
        _firstSlot = 0;
        _dirtyStart = 0;
        _dirtyEnd = 0;
        InvalidateIntensityRange();
        _revision++;
    }

    /// <summary>
    /// Grows the backing store so it can hold at least <paramref name="minimumCapacity"/> points.
    /// Growing unrolls the ring (the copy turns the window back into a plain sequence), so a caller
    /// that knows the final size — a stream's window length — should reserve once instead of letting
    /// the store double its way up.
    /// </summary>
    /// <param name="minimumCapacity">Wanted slot count; ignored when the store is already that large.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimumCapacity"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The requested size does not fit in one array.</exception>
    public void EnsureCapacity(int minimumCapacity)
    {
        if (minimumCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
        if (minimumCapacity <= _capacity)
            return;
        Grow(minimumCapacity);
    }

    /// <summary>
    /// Marks the accumulated change window as uploaded: it restarts (empty) at the current revision.
    /// A backend calls this once it has uploaded <see cref="Dirty"/>; another backend that has not
    /// uploaded up to the new window start has to upload everything.
    /// </summary>
    public void ResetDirty()
    {
        _dirtyStart = 0;
        _dirtyEnd = 0;
        _dirtyFromRevision = _revision;
    }

    // ---- Private write plumbing -------------------------------------------------

    private void PrepareAppend(int count)
    {
        if (IsOrganized)
            throw new InvalidOperationException(
                "Cannot append to an organized point cloud (height > 1): its points form a 2D grid, not a sequence.");
        if (count > 0)
            EnsureCapacity(_count + count);
    }

    private void CommitAppend(int count)
    {
        int start = _count;
        _count += count;
        AddDirty(start, _count);
        InvalidateIntensityRange();
        _revision++;
    }

    /// <summary>Records an in-place change (the point count stays the same) in the change window.</summary>
    private void TouchWindow(int logicalIndex, int count)
    {
        AddDirty(logicalIndex, logicalIndex + count);
        InvalidateIntensityRange();
        _revision++;
    }

    private void AddDirty(int start, int end)
    {
        if (end <= start)
            return;

        if (_dirtyStart == _dirtyEnd)
        {
            _dirtyStart = start;
            _dirtyEnd = end;
            return;
        }

        _dirtyStart = Math.Min(_dirtyStart, start);
        _dirtyEnd = Math.Max(_dirtyEnd, end);
    }

    /// <summary>
    /// Moves the change window down when the oldest points are dropped: the window is indexed by logical
    /// point, so every index shifts by the number of dropped points. Whatever part of the window referred
    /// to dropped points disappears with them (the points are gone, so there is nothing to upload), and a
    /// start pushed below 0 clamps to 0 — at worst that re-uploads points the backend already has.
    /// </summary>
    private void ShiftDirtyWindow(int dropped)
    {
        if (_dirtyStart == _dirtyEnd)
            return;

        int start = Math.Max(0, _dirtyStart - dropped);
        int end = Math.Min(_count, Math.Max(start, _dirtyEnd - dropped));
        bool empty = end <= start;

        _dirtyStart = empty ? 0 : start;
        _dirtyEnd = empty ? 0 : end;
    }

    private void Grow(int minimumCapacity)
    {
        int newCapacity = Math.Max(minimumCapacity, _capacity == 0 ? MinCapacity : _capacity * 2);
        long bytes = (long)newCapacity * _pointStep;
        if (bytes > int.MaxValue)
            throw new InvalidOperationException($"A point cloud of {newCapacity} points does not fit in one array.");

        var grown = new byte[bytes];
        if (_count > 0)
        {
            // Unroll the ring: the window (FirstSlot..end, then 0..FirstSlot) moves to the array start.
            int head = Math.Min(_count, _capacity - _firstSlot);
            Buffer.BlockCopy(_data, _firstSlot * _pointStep, grown, 0, head * _pointStep);
            if (head < _count)
                Buffer.BlockCopy(_data, 0, grown, head * _pointStep, (_count - head) * _pointStep);
        }

        _data = grown;
        _capacity = newCapacity;
        _firstSlot = 0;
        AddDirty(0, _count);   // Every slot moved, so the backend has to re-upload the valid points.
        _revision++;
    }

    private void ValidateRange(int start, int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (start < 0 || start + count > _count)
            throw new ArgumentOutOfRangeException(nameof(start),
                $"Range [{start}, {start + count}) is outside the cloud's {_count} point(s).");
    }

    private void ValidateSlots(int slot, int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (slot < 0 || slot + count > _capacity)
            throw new ArgumentOutOfRangeException(nameof(slot),
                $"Slot range [{slot}, {slot + count}) is outside the {_capacity} slot(s).");
    }

    private void EnsureXyz()
    {
        if (_xField is null || _yField is null || _zField is null)
            throw new InvalidOperationException("Point cloud has no x/y/z position fields.");
    }

    /// <summary>
    /// The accumulated change window reported by <see cref="Dirty"/>: the logical points
    /// [<paramref name="Start"/>, Start + Count) changed between revision
    /// <paramref name="FromRevision"/> and <paramref name="Revision"/>.
    /// </summary>
    /// <param name="FromRevision">Revision the window started accumulating from — the revision a backend must have uploaded to be allowed to use the range.</param>
    /// <param name="Revision">Current revision of the cloud.</param>
    /// <param name="Start">First changed logical point index.</param>
    /// <param name="Count">Number of changed logical points (0 when nothing changed).</param>
    public readonly record struct DirtyWindow(long FromRevision, long Revision, int Start, int Count);

    /// <summary>A physically contiguous run of point slots, as reported by <see cref="GetSlotRuns"/>.</summary>
    /// <param name="Slot">First physical slot of the run.</param>
    /// <param name="Count">Number of slots in the run.</param>
    public readonly record struct SlotRun(int Slot, int Count);

    /// <summary>
    /// Creates an empty unstructured (height 1) cloud meant to be filled incrementally through
    /// <see cref="AddPoint(Vector3)"/> and friends. Its backing store starts with
    /// <paramref name="initialCapacity"/> slots and grows by doubling from there.
    /// </summary>
    /// <param name="initialCapacity">Slots to reserve up front (a stream's window length avoids regrowth; 0 starts empty).</param>
    /// <param name="withColor">When true the layout carries an r/g/b float channel, so points can be appended with a color.</param>
    /// <param name="frameId">Optional source/coordinate frame identifier.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is negative.</exception>
    public static PointCloud2Data CreateMutable(int initialCapacity = 0, bool withColor = false, string? frameId = null)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        int pointStep = withColor ? 24 : 12;
        if ((long)initialCapacity * pointStep > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity),
                $"A cloud of {initialCapacity} points does not fit in one array.");

        PointField[] fields = withColor
            ? new[]
            {
                new PointField("x", 0, PointFieldDataType.Float32),
                new PointField("y", 4, PointFieldDataType.Float32),
                new PointField("z", 8, PointFieldDataType.Float32),
                new PointField("r", 12, PointFieldDataType.Float32),
                new PointField("g", 16, PointFieldDataType.Float32),
                new PointField("b", 20, PointFieldDataType.Float32),
            }
            : new[]
            {
                new PointField("x", 0, PointFieldDataType.Float32),
                new PointField("y", 4, PointFieldDataType.Float32),
                new PointField("z", 8, PointFieldDataType.Float32),
            };

        return new PointCloud2Data(fields, new byte[initialCapacity * pointStep], pointStep, frameId, height: 1)
        {
            // The store is reserved, not filled: a freshly created cloud holds no points yet (the
            // constructor, which wraps an existing buffer, counts every slot as a point).
            _count = 0,
        };
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
