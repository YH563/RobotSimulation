using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 箭头（<see cref="GameObject"/>，模型通道）：沿局部 +Z 的实心箭头
/// （尾端 z=0 … 尖端 z=Length），由圆柱杆 + 圆锥头合并为单个网格。
/// 用 <see cref="Transform"/> 可自由放置/指向任意方向。
/// </summary>
public sealed class Arrow : GameObject
{
    /// <summary>箭头总长。</summary>
    public float Length { get; }

    public Arrow(
        float length = 1f,
        float shaftRadius = 0.02f,
        float headRadius = 0.06f,
        float headLength = 0.22f,
        Vector4? color = null,
        int segments = 24,
        string? name = null)
        : base(
            CreateArrowMesh(length, shaftRadius, headRadius, headLength, segments),
            new MaterialData { BaseColor = color ?? new Vector4(1f, 0.62f, 0.1f, 1f) },
            name ?? nameof(Arrow))
    {
        Length = length;
    }

    /// <summary>
    /// 独立生成箭头网格数据（供外部共享复用时调用，例如各对象局部坐标系使用同一份单位箭头）。
    /// </summary>
    public static MeshData CreateArrowMesh(
        float length = 1f,
        float shaftRadius = 0.02f,
        float headRadius = 0.06f,
        float headLength = 0.22f,
        int segments = 24)
        => BuildMesh(length, shaftRadius, headRadius, headLength, segments);

    private static MeshData BuildMesh(float length, float shaftRadius, float headRadius, float headLength, int segments)
    {
        if (length <= 0f || float.IsNaN(length) || float.IsInfinity(length))
            throw new ArgumentOutOfRangeException(nameof(length), length, "箭长必须为正的有限数值。");
        if (headRadius <= shaftRadius)
            throw new ArgumentException("headRadius 应大于 shaftRadius（否则锥头不可见）。", nameof(headRadius));
        if (headLength <= 0f || headLength >= length)
            throw new ArgumentOutOfRangeException(nameof(headLength), headLength, "headLength 应大于 0 且小于总长。");

        float shaftLen = length - headLength;

        // 圆柱杆（CreateCylinder 轴沿 +Z、跨度为 -l/2..+l/2），中心抬到 z=shaftLen/2 → 尾端 0..shaftLen
        MeshData shaft = Primitives.CreateCylinder(shaftRadius, shaftLen, segments);
        var head = Primitives.CreateCone(headRadius, headLength, segments);   // 跨 -h/2..+h/2

        var merged = new MeshData();
        Append(merged, shaft, new Vector3(0f, 0f, shaftLen * 0.5f));
        Append(merged, head, new Vector3(0f, 0f, shaftLen + headLength * 0.5f));
        return merged;
    }

    /// <summary>把 src 网格整体平移 offset 后追加到 target（索引自动偏移）。</summary>
    private static void Append(MeshData target, MeshData src, Vector3 offset)
    {
        uint baseIndex = (uint)target.VertexCount;
        for (int v = 0; v < src.VertexCount; v++)
        {
            target.AddVertex(
                src.Positions[v] + offset,
                src.Normals[v],
                src.Uvs[v],
                src.Tangents[v]);
        }

        for (int i = 0; i < src.Indices.Count; i += 3)
        {
            target.AddTriangle(
                baseIndex + src.Indices[i],
                baseIndex + src.Indices[i + 1],
                baseIndex + src.Indices[i + 2]);
        }
    }
}
