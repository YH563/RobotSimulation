using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
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
///
/// A cloud can also grow, which is what a live sensor stream needs: <see cref="Append(Vector3)"/> and
/// <see cref="AppendRange(IEnumerable{Vector3})"/> add to the existing buffer instead of replacing it,
/// so the renderer uploads just the new points. With <see cref="MaxPoints"/> set, the cloud becomes a
/// sliding window: once the limit is reached the oldest points are dropped, which costs nothing beyond
/// moving the ring start (no point data is copied and none is re-uploaded).
/// </summary>
public sealed class PointCloud : GameObject
{
    /// <summary>Creates an empty point cloud; fill it via <see cref="SetPoints"/>, <see cref="SetData"/>, <see cref="FromFile"/> or the append methods.</summary>
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
        PointData = data ?? PointCloud2Data.CreateMutable();
    }

    /// <summary>
    /// Upper bound on the point count (0 = unbounded). Exceeding it drops the oldest points, which turns
    /// the cloud into a sliding window over a stream without any cost in the data layer.
    /// </summary>
    public int MaxPoints { get; set; }

    /// <summary>The backing cloud data (never null unless <see cref="GameObject.PointData"/> was cleared from outside).</summary>
    /// <exception cref="InvalidOperationException">The node has no backing data.</exception>
    public PointCloud2Data Data => PointData ?? throw new InvalidOperationException("Point cloud has no backing data.");

    /// <summary>Number of points currently held.</summary>
    public int Count => PointData?.Count ?? 0;

    // ------------------------------------------------------------------
    // Incremental updates
    //
    // These mutate the existing backing data, which is what lets the renderer upload the appended points
    // instead of the cloud. Replacing the data (SetData/SetPoints) remains the right call when the content
    // changes shape, e.g. switching to a different file.
    // ------------------------------------------------------------------

    /// <summary>Appends one point, writing only its position (other channels of the layout stay zero).</summary>
    /// <param name="point">Position in the node's local space.</param>
    /// <exception cref="InvalidOperationException">The backing data cannot take points (no x/y/z fields, or organized).</exception>
    public void Append(Vector3 point)
    {
        MakeRoomFor(1);
        Data.AddPoint(point);
        TrimToMaxPoints();
    }

    /// <summary>
    /// Appends several points, writing only their positions. Arrays and lists are appended without
    /// copying them; any other sequence is buffered once.
    /// </summary>
    /// <param name="points">Positions to append, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The backing data cannot take points (no x/y/z fields, or organized).</exception>
    public void AppendRange(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        if (points is Vector3[] array)
        {
            AppendSpan(array);
            return;
        }

        var buffer = points as List<Vector3> ?? new List<Vector3>(points);
        AppendSpan(CollectionsMarshal.AsSpan(buffer));
    }

    private void AppendSpan(ReadOnlySpan<Vector3> points)
    {
        MakeRoomFor(points.Length);
        Data.AddPoints(points);
        TrimToMaxPoints();
    }

    /// <summary>Overwrites the position of an existing point; its other channels are left untouched.</summary>
    /// <param name="index">Logical index (0 = oldest point).</param>
    /// <param name="position">New position.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside [0, Count).</exception>
    /// <exception cref="InvalidOperationException">The backing data has no x/y/z fields.</exception>
    public void SetPoint(int index, Vector3 position) => Data.SetPoint(index, position);

    /// <summary>Reserves room for <paramref name="capacity"/> points, so a growing cloud reallocates at most once.</summary>
    /// <param name="capacity">Point count to reserve room for.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public void Reserve(int capacity) => Data.EnsureCapacity(capacity);

    /// <summary>Drops the oldest <paramref name="count"/> points (O(1): nothing is copied or re-uploaded).</summary>
    /// <param name="count">Number of oldest points to drop.</param>
    public void RemoveOldest(int count) => Data.RemoveOldest(count);

    /// <summary>Keeps only the newest <paramref name="maxPoints"/> points.</summary>
    /// <param name="maxPoints">Upper bound on the point count.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPoints"/> is negative.</exception>
    public void TrimTo(int maxPoints) => Data.TrimTo(maxPoints);

    /// <summary>Drops every point, keeping the allocated capacity for the next stream.</summary>
    public void Clear() => Data.Clear();

    private void TrimToMaxPoints()
    {
        if (MaxPoints > 0 && Data.Count > MaxPoints)
            Data.RemoveOldest(Data.Count - MaxPoints);
    }

    /// <summary>
    /// Drops the oldest points up front so <paramref name="count"/> more fit inside the window. Doing it
    /// before the append (not only after) is what keeps a sliding window at its steady-state size: a
    /// full window never needs one extra slot, so it never reallocates and never rebuilds its GPU buffer.
    /// </summary>
    private void MakeRoomFor(int count)
    {
        if (MaxPoints <= 0)
            return;

        int over = Data.Count + count - MaxPoints;
        if (over > 0)
            Data.RemoveOldest(Math.Min(over, Data.Count));
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
