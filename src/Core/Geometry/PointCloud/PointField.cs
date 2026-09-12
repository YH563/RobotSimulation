using System;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Description of one channel (field) inside a <see cref="PointCloud2Data"/> point.
/// Mirrors <c>sensor_msgs/PointCloud2</c> PointField: a field name, a byte offset
/// inside every point, a data type, and an element count (how many successive values
/// of that type the field holds per point).
/// </summary>
public sealed class PointField
{
    /// <summary>Field / channel name, e.g. <c>x</c>, <c>rgb</c>, <c>intensity</c>.</summary>
    public string Name { get; }

    /// <summary>Byte offset of the field inside each point.</summary>
    public int Offset { get; }

    /// <summary>Data type of one element of the field.</summary>
    public PointFieldDataType DataType { get; }

    /// <summary>Number of elements of <see cref="DataType"/> the field holds per point.</summary>
    public int Count { get; }

    /// <summary>Creates one channel description.</summary>
    /// <param name="name">Channel name, e.g. <c>x</c>, <c>rgb</c>, <c>intensity</c>.</param>
    /// <param name="offset">Byte offset of the field inside each point.</param>
    /// <param name="dataType">Type of one element.</param>
    /// <param name="count">Number of elements of that type per point.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative or <paramref name="count"/> is below 1.</exception>
    public PointField(string name, int offset, PointFieldDataType dataType, int count = 1)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        Offset = offset;
        DataType = dataType;
        Count = count;
    }

    /// <summary>Byte size of a single element of <paramref name="type"/>.</summary>
    public static int GetElementSize(PointFieldDataType type) => type switch
    {
        PointFieldDataType.Int8 or PointFieldDataType.UInt8 => 1,
        PointFieldDataType.Int16 or PointFieldDataType.UInt16 => 2,
        PointFieldDataType.Int32 or PointFieldDataType.UInt32 or PointFieldDataType.Float32 => 4,
        PointFieldDataType.Float64 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Total byte footprint of this field per point (element size × count).</summary>
    public int Size => GetElementSize(DataType) * Count;
}
