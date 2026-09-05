using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// rviz 风格网格地面（<see cref="GameObject"/>，线条通道）：位于 XY 平面（z = 0），
/// 法线一侧向上；由 GL_LINES 线段组成，不参与光照。用于替换实心地面作为机器人参考平面。
/// </summary>
public sealed class Grid : GameObject
{
    /// <summary>单格边长（米）。</summary>
    public float CellSize { get; }

    /// <summary>中心往每个方向的格数（总跨度 = 2 × CellCount × CellSize）。</summary>
    public int CellCount { get; }

    public Grid(float cellSize = 1f, int cellCount = 10, Vector4? color = null, string? name = "Grid")
        : base(null, null, name ?? nameof(Grid))
    {
        if (cellSize <= 0f || float.IsNaN(cellSize) || float.IsInfinity(cellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "格边长必须为正的有限数值。");
        if (cellCount < 1)
            throw new ArgumentOutOfRangeException(nameof(cellCount), cellCount, "格数不能小于 1。");

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
            lines.AddSegment(new Vector3(t, -half, 0f), new Vector3(t, half, 0f));  // 沿 Y 的网格线
            lines.AddSegment(new Vector3(-half, t, 0f), new Vector3(half, t, 0f));  // 沿 X 的网格线
        }
        return lines;
    }
}
