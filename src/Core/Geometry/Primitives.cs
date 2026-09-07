using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 基本图元网格的共享生成实现（internal，非公共 API）：图元 GameObject 派生类
/// （Box / Sphere / Cylinder / Capsule / GroundPlane）在构造时调用本类静态方法。
/// 输出约定：
///  - 全部为“局部坐标”网格；圆柱/胶囊/球体的回转轴沿 +Z（与 URDF/ROS 几何语义一致）；
///  - 三角形一律保证“从外表面看为 CCW”，可直接配合 CullFace.Back 渲染；
///  - 顶点格式为 MeshData：pos(3) + uv(2) + normal(3) + tangent(3)。
/// </summary>
internal static class Primitives
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
    internal static MeshData CreateBox(float width, float height, float depth)
    {
        EnsurePositive(nameof(width), width);
        EnsurePositive(nameof(height), height);
        EnsurePositive(nameof(depth), depth);

        var mesh = new MeshData();

        // (外法线 N, 面的中心偏移 CenterOffset, 切线 U, 面内两个尺寸 SizeU/SizeV)。
        // 副法线 V = cross(N, U)，满足 U×V = N，因此四角点 (c00,c10,c11,c01) 排列后三角形天然 CCW。
        // 关键：每个面的矩形必须沿法线偏移到 ±对应半宽（否则六个面都过原点，退化为"扁十字"）。
        Span<(Vector3 Normal, Vector3 CenterOffset, Vector3 Tangent, float SizeU, float SizeV)> faces =
            stackalloc (Vector3 Normal, Vector3 CenterOffset, Vector3 Tangent, float SizeU, float SizeV)[]
            {
                ( Vector3.UnitX,  new Vector3( width * 0.5f, 0f, 0f), Vector3.UnitY, height, depth),   // +X
                (-Vector3.UnitX,  new Vector3(-width * 0.5f, 0f, 0f), Vector3.UnitY, height, depth),   // -X
                ( Vector3.UnitY,  new Vector3(0f,  height * 0.5f, 0f), Vector3.UnitX, width,  depth),  // +Y
                (-Vector3.UnitY,  new Vector3(0f, -height * 0.5f, 0f), Vector3.UnitX, width,  depth),  // -Y
                ( Vector3.UnitZ,  new Vector3(0f, 0f,  depth * 0.5f), Vector3.UnitX, width,  height),  // +Z
                (-Vector3.UnitZ,  new Vector3(0f, 0f, -depth * 0.5f), Vector3.UnitX, width,  height),  // -Z
            };

        foreach (var (normal, centerOffset, tangent, sizeU, sizeV) in faces)
        {
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            Vector3 hu = tangent * (sizeU * 0.5f);
            Vector3 hv = bitangent * (sizeV * 0.5f);

            uint i00 = mesh.AddVertex(centerOffset - hu - hv, normal, new Vector2(0f, 0f), tangent);
            uint i10 = mesh.AddVertex(centerOffset + hu - hv, normal, new Vector2(1f, 0f), tangent);
            uint i11 = mesh.AddVertex(centerOffset + hu + hv, normal, new Vector2(1f, 1f), tangent);
            uint i01 = mesh.AddVertex(centerOffset - hu + hv, normal, new Vector2(0f, 1f), tangent);

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
    internal static MeshData CreateSphere(float radius, int segments = 32, int rings = 16)
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

    // =====================================================================
    // 圆柱体
    // =====================================================================

    /// <summary>
    /// 生成圆柱：回转轴沿 +Z（URDF/ROS 语义），高为 <paramref name="length"/>。
    /// 侧面平滑共享法线；顶/底盖使用各自独立的硬边法线（±Z）。
    /// </summary>
    internal static MeshData CreateCylinder(float radius, float length, int segments = 32)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(length), length);
        EnsureAtLeast(nameof(segments), segments, 3);

        var mesh = new MeshData();
        float half = length * 0.5f;

        // ---- 侧面：底部环(z=-half) + 顶部环(z=+half)，各 segments+1 列 ----
        uint bottom = (uint)mesh.VertexCount;
        for (int i = 0; i <= segments; i++)
        {
            float theta = (i % segments) * MathF.Tau / segments;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            mesh.AddVertex(
                new Vector3(radius * cos, radius * sin, -half),
                new Vector3(cos, sin, 0f),
                new Vector2(i / (float)segments, 0f),
                new Vector3(-sin, cos, 0f));
        }

        uint top = (uint)mesh.VertexCount;
        for (int i = 0; i <= segments; i++)
        {
            float theta = (i % segments) * MathF.Tau / segments;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            mesh.AddVertex(
                new Vector3(radius * cos, radius * sin, half),
                new Vector3(cos, sin, 0f),
                new Vector2(i / (float)segments, 1f),
                new Vector3(-sin, cos, 0f));
        }

        // 侧面条带（外法线参考取径向方向，忽略 z 分量）
        for (int i = 0; i < segments; i++)
        {
            uint b0 = bottom + (uint)i;
            uint b1 = bottom + (uint)(i + 1);
            uint t0 = top + (uint)i;
            uint t1 = top + (uint)(i + 1);

            AddOrientedTriangle(mesh, t0, b0, b1, NormalizeXY(Centroid(mesh, t0, b0, b1)));
            AddOrientedTriangle(mesh, t0, b1, t1, NormalizeXY(Centroid(mesh, t0, b1, t1)));
        }

        // ---- 顶盖（法线 +Z）：中心 + 独立 rim 环 ----
        uint topCenter = mesh.AddVertex(
            new Vector3(0f, 0f, half), Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);

        var topRim = new uint[segments];
        for (int i = 0; i < segments; i++)
        {
            float theta = i * MathF.Tau / segments;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            topRim[i] = mesh.AddVertex(
                new Vector3(radius * cos, radius * sin, half),
                Vector3.UnitZ,
                new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f),
                new Vector3(-sin, cos, 0f));
        }

        for (int i = 0; i < segments; i++)
            AddOrientedTriangle(mesh, topCenter, topRim[i], topRim[(i + 1) % segments], Vector3.UnitZ);

        // ---- 底盖（法线 -Z）：中心 + 独立 rim 环 ----
        uint bottomCenter = mesh.AddVertex(
            new Vector3(0f, 0f, -half), -Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);

        var bottomRim = new uint[segments];
        for (int i = 0; i < segments; i++)
        {
            float theta = i * MathF.Tau / segments;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            bottomRim[i] = mesh.AddVertex(
                new Vector3(radius * cos, radius * sin, -half),
                -Vector3.UnitZ,
                new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f),
                new Vector3(-sin, cos, 0f));
        }

        for (int i = 0; i < segments; i++)
            AddOrientedTriangle(mesh, bottomCenter, bottomRim[(i + 1) % segments], bottomRim[i], -Vector3.UnitZ);

        return mesh;
    }

    // =====================================================================
    // 胶囊体
    // =====================================================================

    /// <summary>
    /// 生成胶囊体：回转轴沿 +Z（URDF/ROS 语义）。<paramref name="length"/> 为中间
    /// 圆柱段长度（不含两端半球帽），总高 = length + 2·radius。
    /// </summary>
    internal static MeshData CreateCapsule(float radius, float length, int segments = 32, int rings = 8)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(length), length);
        EnsureAtLeast(nameof(segments), segments, 3);
        EnsureAtLeast(nameof(rings), rings, 2);

        // 圆柱段长度为 0 时退化为球
        if (length <= 1e-5f)
            return CreateSphere(radius, segments, Math.Max(2, rings));

        var mesh = new MeshData();
        float half = length * 0.5f;
        float totalHeight = length + 2f * radius;

        // 每半球壳 rings 条纬线，中部圆柱段再补若干行
        int midRows = Math.Max(2, (int)MathF.Ceiling(length / radius));
        int rowSteps = 2 * rings + midRows;   // 相邻行之间的"纬度步数"

        // 三角片外法线参考：球帽区指向球心方向，中部指向回转轴径向
        Vector3 ArcOutward(Vector3 c)
        {
            if (c.Z >= half)
                return Vector3.Normalize(c - new Vector3(0f, 0f, half));
            if (c.Z <= -half)
                return Vector3.Normalize(c - new Vector3(0f, 0f, -half));
            return NormalizeXY(c);
        }

        // 沿轮廓自南极到北极的弧长占比（用于 UV 的 v 坐标，保证贴图不拉伸）
        float ArcFraction(float z)
        {
            float totalArc = radius * MathF.PI + length;
            if (z <= -half)
            {
                float delta = z + half;                              // ∈ [-radius, 0]
                return radius * (MathF.Asin(delta / radius) + MathF.PI * 0.5f) / totalArc;
            }
            if (z >= half)
            {
                float delta = z - half;                              // ∈ [0, radius]
                float s = radius * MathF.PI * 0.5f + length + radius * MathF.Asin(delta / radius);
                return s / totalArc;
            }
            return (radius * MathF.PI * 0.5f + (z + half)) / totalArc;
        }

        var rowBase = new uint[rowSteps + 1];
        var rowCols = new int[rowSteps + 1];

        for (int k = 0; k <= rowSteps; k++)
        {
            bool pole = k == 0 || k == rowSteps;
            float z = k == rowSteps
                ? totalHeight * 0.5f
                : -totalHeight * 0.5f + totalHeight * k / rowSteps;

            // 该高度处的截面半径 rho 与法线的水平分量比例 nxy（解析值）
            float rho, nxy;
            if (z >= half)
            {
                float d = MathF.Min(z - half, radius);
                rho = MathF.Sqrt(radius * radius - d * d);
                nxy = rho / radius;              // 法线 z 分量 = d / radius
            }
            else if (z <= -half)
            {
                float d = MathF.Min(-(z + half), radius);
                rho = MathF.Sqrt(radius * radius - d * d);
                nxy = rho / radius;              // 法线 z 分量 = -d / radius
            }
            else
            {
                rho = radius;
                nxy = 1f;
            }

            int columns = pole ? segments : segments + 1;
            rowBase[k] = (uint)mesh.VertexCount;
            rowCols[k] = columns;

            for (int i = 0; i < columns; i++)
            {
                int slice = i % segments;
                float theta = slice * MathF.Tau / segments;
                float cos = MathF.Cos(theta);
                float sin = MathF.Sin(theta);

                float u = pole ? (slice + 0.5f) / segments : i / (float)segments;
                float v = ArcFraction(z);

                var position = pole
                    ? new Vector3(0f, 0f, z)
                    : new Vector3(rho * cos, rho * sin, z);

                var normal = pole
                    ? new Vector3(0f, 0f, k == 0 ? -1f : 1f)
                    : z >= half
                        ? new Vector3(nxy * cos, nxy * sin, (z - half) / radius)
                        : z <= -half
                            ? new Vector3(nxy * cos, nxy * sin, -(z + half) / radius)
                            : new Vector3(cos, sin, 0f);

                var tangent = pole
                    ? Vector3.UnitX
                    : new Vector3(-sin, cos, 0f);

                mesh.AddVertex(position, normal, new Vector2(u, v), tangent);
            }
        }

        uint Get(int row, int col) => rowBase[row] + (uint)(col % rowCols[row]);

        for (int k = 0; k < rowSteps; k++)
        {
            bool fromPole = k == 0;              // 南极扇区
            bool toPole = k == rowSteps - 1;     // 北极扇区

            for (int i = 0; i < segments; i++)
            {
                if (fromPole)
                {
                    uint p = Get(0, i);
                    uint a = Get(1, i);
                    uint b = Get(1, i + 1);
                    AddOrientedTriangle(mesh, p, a, b, ArcOutward(Centroid(mesh, p, a, b)));
                }
                else if (toPole)
                {
                    uint a = Get(rowSteps - 1, i);
                    uint b = Get(rowSteps - 1, i + 1);
                    uint p = Get(rowSteps, i);
                    AddOrientedTriangle(mesh, a, b, p, ArcOutward(Centroid(mesh, a, b, p)));
                }
                else
                {
                    uint a = Get(k, i);
                    uint b = Get(k, i + 1);
                    uint c = Get(k + 1, i + 1);
                    uint d = Get(k + 1, i);
                    AddOrientedTriangle(mesh, a, b, c, ArcOutward(Centroid(mesh, a, b, c)));
                    AddOrientedTriangle(mesh, a, c, d, ArcOutward(Centroid(mesh, a, c, d)));
                }
            }
        }

        return mesh;
    }

    // =====================================================================
    // 圆锥（Arrow 箭头等使用；轴沿 +Z，与 URDF/ROS 语义一致）
    // =====================================================================

    /// <summary>
    /// 生成圆锥：回转轴沿 +Z，底面（半径 <paramref name="radius"/>）位于 z = -height/2，
    /// 顶点位于 +height/2。底面封口、侧面含法线。
    /// </summary>
    internal static MeshData CreateCone(float radius, float height, int segments = 24)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(height), height);
        EnsureAtLeast(nameof(segments), segments, 3);

        var mesh = new MeshData();
        float side = MathF.Atan2(radius, height);   // 侧面与水平面的倾角 → 法线
        float cosSide = MathF.Cos(side);
        float sinSide = MathF.Sin(side);

        // 底面环
        var ring = new uint[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * MathF.Tau;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            ring[i] = mesh.AddVertex(
                new Vector3(radius * c, radius * s, -height * 0.5f),
                new Vector3(c * cosSide, s * cosSide, sinSide),
                new Vector2(i / (float)segments, 0f),
                new Vector3(-s, c, 0f));
        }

        uint apex = mesh.AddVertex(new Vector3(0f, 0f, height * 0.5f), Vector3.UnitZ, new Vector2(0.5f, 1f), Vector3.UnitX);

        // 侧面（外法线参考：环上当前顶点的斜向法线）
        for (int i = 0; i < segments; i++)
        {
            uint next = ring[(i + 1) % segments];
            float ai = i / (float)segments * MathF.Tau;
            Vector3 outward = new Vector3(
                MathF.Cos(ai) * cosSide, MathF.Sin(ai) * cosSide, sinSide);
            AddOrientedTriangle(mesh, apex, next, ring[i], outward);
        }

        // 底面封口（朝 -Z）
        uint center = mesh.AddVertex(new Vector3(0f, 0f, -height * 0.5f), -Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);
        for (int i = 0; i < segments; i++)
        {
            uint next = ring[(i + 1) % segments];
            AddOrientedTriangle(mesh, ring[i], next, center, -Vector3.UnitZ);
        }

        return mesh;
    }

    // =====================================================================
    // 平面
    // =====================================================================

    /// <summary>
    /// 生成水平地面：位于 XY 平面（z = 0），法线朝 +Z，宽 <paramref name="width"/>（沿 X）、
    /// 深 <paramref name="depth"/>（沿 Y）。用于 Z-up 引擎（与 URDF/ROS 世界约定一致）。
    /// </summary>
    internal static MeshData CreatePlane(float width, float depth)
    {
        EnsurePositive(nameof(width), width);
        EnsurePositive(nameof(depth), depth);

        var mesh = new MeshData();
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        uint i00 = mesh.AddVertex(new Vector3(-hw, -hd, 0f), Vector3.UnitZ, new Vector2(0f, 0f), Vector3.UnitX);
        uint i10 = mesh.AddVertex(new Vector3( hw, -hd, 0f), Vector3.UnitZ, new Vector2(1f, 0f), Vector3.UnitX);
        uint i11 = mesh.AddVertex(new Vector3( hw,  hd, 0f), Vector3.UnitZ, new Vector2(1f, 1f), Vector3.UnitX);
        uint i01 = mesh.AddVertex(new Vector3(-hw,  hd, 0f), Vector3.UnitZ, new Vector2(0f, 1f), Vector3.UnitX);

        AddOrientedTriangle(mesh, i00, i10, i11, Vector3.UnitZ);
        AddOrientedTriangle(mesh, i00, i11, i01, Vector3.UnitZ);

        return mesh;
    }
}
