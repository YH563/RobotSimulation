namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Data type of a single element in a point cloud field.
/// The numeric values match the <c>datatype</c> ids used by ROS
/// <c>sensor_msgs/PointCloud2</c> and the <c>TYPE</c> tag of PCL PCD files.
/// </summary>
public enum PointFieldDataType : byte
{
    /// <summary>Signed 8-bit integer.</summary>
    Int8 = 1,

    /// <summary>Unsigned 8-bit integer.</summary>
    UInt8 = 2,

    /// <summary>Signed 16-bit integer.</summary>
    Int16 = 3,

    /// <summary>Unsigned 16-bit integer.</summary>
    UInt16 = 4,

    /// <summary>Signed 32-bit integer.</summary>
    Int32 = 5,

    /// <summary>Unsigned 32-bit integer.</summary>
    UInt32 = 6,

    /// <summary>32-bit IEEE float.</summary>
    Float32 = 7,

    /// <summary>64-bit IEEE float.</summary>
    Float64 = 8,
}
