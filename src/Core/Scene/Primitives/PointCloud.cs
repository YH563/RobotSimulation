using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// A point cloud (<see cref="GameObject"/> on the Point pass) that renders a set of 3D points.
/// By default it draws one color with a uniform size; the color can come from the material
/// (<see cref="MaterialData.BaseColor"/>) or, when the backing data carries per-point colors,
/// from the cloud itself (see <see cref="PointCloud2Data.HasColor"/>).
/// Data is either built on the fly via <see cref="SetPoints"/>, set directly via
/// <see cref="SetData"/>, or loaded from a file with <see cref="FromFile"/> (.pcd / .ply).
/// </summary>
public sealed class PointCloud : GameObject
{
    /// <summary>Creates an empty point cloud; fill it via <see cref="SetPoints"/>, <see cref="SetData"/> or <see cref="FromFile"/>.</summary>
    /// <param name="pointSize">Point sprite size in pixels.</param>
    /// <param name="color">Point color used when the data carries no per-point colors; null uses the default orange.</param>
    /// <param name="name">Scene object name (defaults to <c>PointCloud</c>).</param>
    /// <param name="data">Optional backing data; null starts from an empty cloud.</param>
    public PointCloud(float pointSize = 3f, Vector4? color = null, string? name = null,
        PointCloud2Data? data = null)
        : base(null, null, name ?? nameof(PointCloud))
    {
        MaterialData = new MaterialData
        {
            PassKind = RenderPassKind.Point,
            BaseColor = color ?? new Vector4(0.95f, 0.4f, 0.2f, 1f),
        };
        PointSize = pointSize;
        PointData = data ?? PointCloud2Data.FromPositions(Array.Empty<Vector3>());
    }

    /// <summary>Replaces the point set from a list of positions (no per-point colors).</summary>
    public void SetPoints(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));
        PointData = PointCloud2Data.FromPositions(points);
    }

    /// <summary>Replaces the backing point data (e.g. a loaded or self-built structured cloud).</summary>
    public void SetData(PointCloud2Data data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        PointData = data;
    }

    /// <summary>Loads a point cloud from a file (.pcd / .ply) and wraps it as a scene object.</summary>
    public static PointCloud FromFile(string path, float pointSize = 3f, Vector4? color = null, string? name = null)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        PointCloud2Data data = PointCloudIo.Load(path);
        return new PointCloud(pointSize, color, name ?? Path.GetFileNameWithoutExtension(path), data);
    }
}
