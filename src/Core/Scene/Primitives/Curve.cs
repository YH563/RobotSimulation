using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Curve / path (<see cref="GameObject"/>, Line pass): connects a given sequence of points with
/// polylines. A single-color line (from <see cref="MaterialData.BaseColor"/>); when the data changes,
/// call <see cref="SetPoints"/> to rebuild (the renderer re-uploads it once for the new data reference).
/// </summary>
public sealed class Curve : GameObject
{
    /// <summary>Creates a polyline that connects <paramref name="points"/> in order.</summary>
    /// <param name="points">The polyline's vertices (fewer than two points clears the line).</param>
    /// <param name="color">Line color; null uses the default orange.</param>
    /// <param name="name">Scene object name (defaults to <c>Curve</c>).</param>
    public Curve(IEnumerable<Vector3> points, Vector4? color = null, string? name = null)
        : base(null, null, name ?? nameof(Curve))
    {
        MaterialData = new MaterialData
        {
            PassKind = RenderPassKind.Line,
            BaseColor = color ?? new Vector4(1f, 0.55f, 0.1f, 1f),
        };
        LineData = new LineData();
        SetPoints(points);
    }

    /// <summary>Replaces the curve's point sequence (at least 2 points form a line; fewer clears it).</summary>
    public void SetPoints(IEnumerable<Vector3> points)
    {
        if (points is null)
            throw new ArgumentNullException(nameof(points));

        var lines = new LineData();
        Vector3? previous = null;
        foreach (Vector3 p in points)
        {
            if (previous.HasValue)
                lines.AddSegment(previous.Value, p);
            previous = p;
        }
        LineData = lines;
    }
}
