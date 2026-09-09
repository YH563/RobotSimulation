using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// rviz-style grid floor (<see cref="GameObject"/>, Line pass): lies on the XY plane (z = 0) with the
/// normal side up, made of GL_LINES and not lit. Used as a reference plane for robots instead of a
/// solid floor.
/// </summary>
public sealed class Grid : GameObject
{
    /// <summary>Cell edge length (meters).</summary>
    public float CellSize { get; }

    /// <summary>Cell count from the center in each direction (total span = 2 × CellCount × CellSize).</summary>
    public int CellCount { get; }

    public Grid(float cellSize = 1f, int cellCount = 10, Vector4? color = null, string? name = "Grid")
        : base(null, null, name ?? nameof(Grid))
    {
        if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be a positive finite value.");
        if (cellCount < 1)
            throw new ArgumentOutOfRangeException(nameof(cellCount), cellCount, "Cell count cannot be less than 1.");

        CellSize = cellSize;
        CellCount = cellCount;

        LineData = BuildLines(cellSize, cellCount);
        MaterialData = new MaterialData
        {
            PassKind = RenderPassKind.Line,
            BaseColor = color ?? new Vector4(0.30f, 0.30f, 0.35f, 1f),
        };
    }

    private static LineData BuildLines(float cellSize, int cellCount)
    {
        var lines = new LineData();
        float half = cellCount * cellSize;

        for (int i = -cellCount; i <= cellCount; i++)
        {
            float t = i * cellSize;
            lines.AddSegment(new Vector3(t, -half, 0f), new Vector3(t, half, 0f));  // Grid line along Y.
            lines.AddSegment(new Vector3(-half, t, 0f), new Vector3(half, t, 0f));  // Grid line along X.
        }
        return lines;
    }
}
