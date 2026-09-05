using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 基本图元网格生成器（纯 CPU，不依赖 GL）。
/// 输出约定：
///  - 全部为"局部坐标"网格；圆柱/胶囊/球体的回转轴沿 +Z（与 URDF/ROS 几何语义一致）；
///  - 三角形一律保证"从外表面看为 CCW"，可直接配合 CullFace.Back 渲染；
///  - 顶点格式为 MeshData：pos(3) + uv(2) + normal(3) + tangent(3)。
/// </summary>
public static class PrimitiveBuilder
{
    // ---- 参数校验 ----

    private static void EnsurePositive(string name, float value)
    {
        if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, value, $"{name} 必须为正的有限数值。");
    }

    private static void EnsureAtLeast(string name, int value, int min)
    {
        if (value < min)
            throw new ArgumentOutOfRangeException(name, value, $"{name} 不能小于 {min}。");
    }

    // ---- 几何工具 ----

    /// <summary>
    /// 添加一个三角形，并用外法线参考方向 <paramref name="outwardNormal"/>
    /// 自动修正绕序，保证从外侧看为 CCW。outwardNormal 只需"大致朝外"。
    /// </summary>
    private static void AddOrientedTriangle(MeshData mesh, uint ia, uint ib, uint ic, Vector3 outwardNormal)
    {
        Vector3 a = mesh.Positions[(int)ia];
        Vector3 b = mesh.Positions[(int)ib];
        Vector3 c = mesh.Positions[(int)ic];
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, outwardNormal) < 0f)
            (ib, ic) = (ic, ib);
        mesh.AddTriangle(ia, ib, ic);
    }

    /// <summary>三角形三个顶点的几何平均（位于表面内侧一点，用于取外法线参考方向）。</summary>
    private static Vector3 Centroid(MeshData mesh, uint a, uint b, uint c)
        => (mesh.Positions[(int)a] + mesh.Positions[(int)b] + mesh.Positions[(int)c]) / 3f;

    /// <summary>保留 xy 分量并归一化（用于圆柱/胶囊侧面的径向朝外方向）。</summary>
    private static Vector3 NormalizeXY(Vector3 v)
        => Vector3.Normalize(new Vector3(v.X, v.Y, 0f));

    // =====================================================================
    // 立方体（盒体）
    // =====================================================================

    /// <summary>
    /// 生成立方体，尺寸分别对应 x/y/z 轴宽度。每面 4 个独立顶点，法线为硬边面法线。
    /// </summary>
    public static MeshData CreateBox(float width, float height, float depth)
    {
        EnsurePositive(nameof(width), width);
        EnsurePositive(nameof(height), height);
        EnsurePositive(nameof(depth), depth);

        var mesh = new MeshData();

        // (外法线 N, 切线 U)。副法线 V = cross(N, U)，满足 U×V = N，
        // 因此四角点 (c00,c10,c11,c01) 排列后三角形天然 CCW。
        Span<(Vector3 Normal, Vector3 Tangent, float SizeU, float SizeV)> faces =
            stackalloc (Vector3 Normal, Vector3 Tangent, float SizeU, float SizeV)[]
            {
                ( Vector3.UnitX,  Vector3.UnitY, height, depth),   // +X
                (-Vector3.UnitX,  Vector3.UnitY, height, depth),   // -X
                ( Vector3.UnitY,  Vector3.UnitX, width,  depth),   // +Y
                (-Vector3.UnitY,  Vector3.UnitX, width,  depth),   // -Y
                ( Vector3.UnitZ,  Vector3.UnitX, width,  height),  // +Z
                (-Vector3.UnitZ,  Vector3.UnitX, width,  height),  // -Z
            };

        foreach (var (normal, tangent, sizeU, sizeV) in faces)
        {
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            Vector3 hu = tangent * (sizeU * 0.5f);
            Vector3 hv = bitangent * (sizeV * 0.5f);

            uint i00 = mesh.AddVertex(-hu - hv, normal, new Vector2(0f, 0f), tangent);
            uint i10 = mesh.AddVertex( hu - hv, normal, new Vector2(1f, 0f), tangent);
            uint i11 = mesh.AddVertex( hu + hv, normal, new Vector2(1f, 1f), tangent);
            uint i01 = mesh.AddVertex(-hu + hv, normal, new Vector2(0f, 1f), tangent);

            AddOrientedTriangle(mesh, i00, i10, i11, normal);
            AddOrientedTriangle(mesh, i00, i11, i01, normal);
        }

        return mesh;
    }

    // =====================================================================
    // 球体
    // =====================================================================

    /// <summary>
    /// 生成经纬球（UV 球）。极点行的每"列"是独立顶点，便于纹理沿经线闭合。
    /// <paramref name="segments"/> 为圆周分段数，<paramref name="rings"/> 为纬线分段数。
    /// </summary>
    public static MeshData CreateSphere(float radius, int segments = 32, int rings = 16)
    {
        EnsurePositive(nameof(radius), radius);
        EnsureAtLeast(nameof(segments), segments, 3);
        EnsureAtLeast(nameof(rings), rings, 2);

        var mesh = new MeshData();

        // 纬线行 k = 0..rings，纬度 φ = π/2 - k·(π/rings)（0=北极，rings=南极）。
        // 中间行含 segments+1 列（末列与首列重合，闭合绕回）；极点行仅 segments 列。
        var rowBase = new uint[rings + 1];
        var rowCols = new int[rings + 1];

        for (int k = 0; k <= rings; k++)
        {
            bool pole = k == 0 || k == rings;
            float phi = MathF.PI * 0.5f - k * (MathF.PI / rings);
            float z = radius * MathF.Sin(phi);
            float rho = radius * MathF.Cos(phi);

            int columns = pole ? segments : segments + 1;
            rowBase[k] = (uint)mesh.VertexCount;
            rowCols[k] = columns;

            for (int i = 0; i < columns; i++)
            {
                int slice = i % segments;
                float theta = slice * MathF.Tau / segments;
                float u = pole ? (slice + 0.5f) / segments : i / (float)segments;
                float v = 1f - k / (float)rings;

                var position = pole
                    ? new Vector3(0f, 0f, z)
                    : new Vector3(rho * MathF.Cos(theta), rho * MathF.Sin(theta), z);

                var normal = pole
                    ? new Vector3(0f, 0f, MathF.Sign(z))
                    : position / radius;

                var tangent = pole
                    ? Vector3.UnitX
                    : new Vector3(-MathF.Sin(theta), MathF.Cos(theta), 0f);

                mesh.AddVertex(position, normal, new Vector2(u, v), tangent);
            }
        }

        uint Get(int row, int col) => rowBase[row] + (uint)(col % rowCols[row]);

        for (int k = 0; k < rings; k++)
        {
            bool fromPole = k == 0;           // 北极扇区
            bool toPole = k == rings - 1;     // 南极扇区

            for (int i = 0; i < segments; i++)
            {
                if (fromPole)
                {
                    uint p = Get(0, i);
                    uint a = Get(1, i);
                    uint b = Get(1, i + 1);
                    AddOrientedTriangle(mesh, p, a, b, Vector3.Normalize(Centroid(mesh, p, a, b)));
                }
                else if (toPole)
                {
                    uint a = Get(rings - 1, i);
                    uint b = Get(rings - 1, i + 1);
                    uint p = Get(rings, i);
                    AddOrientedTriangle(mesh, a, b, p, Vector3.Normalize(Centroid(mesh, a, b, p)));
                }
                else
                {
                    uint a = Get(k, i);
                    uint b = Get(k, i + 1);
                    uint c = Get(k + 1, i + 1);
                    uint d = Get(k + 1, i);
                    AddOrientedTriangle(mesh, a, b, c, Vector3.Normalize(Centroid(mesh, a, b, c)));
                    AddOrientedTriangle(mesh, a, c, d, Vector3.Normalize(Centroid(mesh, a, c, d)));
                }
            }
        }

        return mesh;
    }
}
