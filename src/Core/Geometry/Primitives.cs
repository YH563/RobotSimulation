using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Shared mesh generation for basic primitives (internal, not a public API): the primitive
/// GameObject derived classes (Box / Sphere / Cylinder / Capsule / GroundPlane) call these static
/// methods at construction. Output conventions:
///  - All are "local coordinate" meshes; the revolution axis of cylinders/capsules/spheres is +Z
///    (consistent with URDF/ROS geometry);
///  - Triangles are always CCW viewed from outside, so they can be rendered with CullFace.Back;
///  - Vertex format is MeshData: pos(3) + uv(2) + normal(3) + tangent(3).
/// </summary>
internal static class Primitives
{
    // ---- Argument validation ----

    private static void EnsurePositive(string name, float value)
    {
        if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a positive finite value.");
    }

    private static void EnsureAtLeast(string name, int value, int min)
    {
        if (value < min)
            throw new ArgumentOutOfRangeException(name, value, $"{name} cannot be less than {min}.");
    }

    // ---- Geometry helpers ----

    /// <summary>
    /// Adds a triangle and auto-corrects the winding using the outward-normal reference
    /// <paramref name="outwardNormal"/>, guaranteeing CCW viewed from outside. The reference only
    /// needs to point roughly outward.
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

    /// <summary>Geometric average of a triangle's three vertices (a point slightly inside the surface, used as an outward normal reference).</summary>
    private static Vector3 Centroid(MeshData mesh, uint a, uint b, uint c)
        => (mesh.Positions[(int)a] + mesh.Positions[(int)b] + mesh.Positions[(int)c]) / 3f;

    /// <summary>Keeps the xy components and normalizes (used for the radial outward direction of cylinder/capsule sides).</summary>
    private static Vector3 NormalizeXY(Vector3 v)
        => Vector3.Normalize(new Vector3(v.X, v.Y, 0f));

    // =====================================================================
    // Box
    // =====================================================================

    /// <summary>
    /// Creates a box with widths along the x/y/z axes. Each face uses 4 independent vertices with
    /// hard-edged face normals.
    /// </summary>
    internal static MeshData CreateBox(float width, float height, float depth)
    {
        EnsurePositive(nameof(width), width);
        EnsurePositive(nameof(height), height);
        EnsurePositive(nameof(depth), depth);

        var mesh = new MeshData();

        // (Outward normal N, face center offset, tangent U, in-plane sizes SizeU/SizeV).
        // Bitangent V = cross(N, U) satisfies U×V = N, so the four corners (c00,c10,c11,c01) yield
        // naturally CCW triangles. Key point: each face rectangle must be offset along its normal to
        // ± the corresponding half-width (otherwise all six faces pass through the origin, degenerating
        // into a flat "cross").
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
    // Sphere
    // =====================================================================

    /// <summary>
    /// Generates a UV sphere. Each "column" in a pole row is an independent vertex so the texture can
    /// wrap around the meridian. <paramref name="segments"/> is the circumferential segment count and
    /// <paramref name="rings"/> the latitude segment count.
    /// </summary>
    internal static MeshData CreateSphere(float radius, int segments = 32, int rings = 16)
    {
        EnsurePositive(nameof(radius), radius);
        EnsureAtLeast(nameof(segments), segments, 3);
        EnsureAtLeast(nameof(rings), rings, 2);

        var mesh = new MeshData();

        // Latitude rows k = 0..rings, latitude φ = π/2 - k·(π/rings) (0 = north pole, rings = south pole).
        // Middle rows contain segments+1 columns (last column coincides with the first, closing the wrap);
        // pole rows contain only segments columns.
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
            bool fromPole = k == 0;           // North pole sector
            bool toPole = k == rings - 1;     // South pole sector

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
    // Cylinder
    // =====================================================================

    /// <summary>
    /// Generates a cylinder whose revolution axis is +Z (URDF/ROS semantics) with height
    /// <paramref name="length"/>. The side shares smooth normals; the top/bottom caps use their own
    /// hard-edged normals (±Z).
    /// </summary>
    internal static MeshData CreateCylinder(float radius, float length, int segments = 32)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(length), length);
        EnsureAtLeast(nameof(segments), segments, 3);

        var mesh = new MeshData();
        float half = length * 0.5f;

        // ---- Side: bottom ring (z=-half) + top ring (z=+half), with segments+1 columns each ----
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

        // Side strip (outward normal reference takes the radial direction, ignoring the z component)
        for (int i = 0; i < segments; i++)
        {
            uint b0 = bottom + (uint)i;
            uint b1 = bottom + (uint)(i + 1);
            uint t0 = top + (uint)i;
            uint t1 = top + (uint)(i + 1);

            AddOrientedTriangle(mesh, t0, b0, b1, NormalizeXY(Centroid(mesh, t0, b0, b1)));
            AddOrientedTriangle(mesh, t0, b1, t1, NormalizeXY(Centroid(mesh, t0, b1, t1)));
        }

        // ---- Top cap (facing +Z) ----
        uint topCenter = mesh.AddVertex(new Vector3(0f, 0f, half), Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);
        for (int i = 0; i < segments; i++)
        {
            uint a = top + (uint)i;
            uint b = top + (uint)((i + 1) % segments);
            AddOrientedTriangle(mesh, topCenter, a, b, Vector3.UnitZ);
        }

        // ---- Bottom cap (facing -Z) ----
        uint bottomCenter = mesh.AddVertex(new Vector3(0f, 0f, -half), -Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);
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
    // Capsule
    // =====================================================================

    /// <summary>
    /// Generates a capsule whose revolution axis is +Z (URDF/ROS semantics). <paramref name="length"/>
    /// is the middle cylinder segment length (excluding the hemispherical caps), so the total height is
    /// length + 2·radius.
    /// </summary>
    internal static MeshData CreateCapsule(float radius, float length, int segments = 32, int rings = 8)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(length), length);
        EnsureAtLeast(nameof(segments), segments, 3);
        EnsureAtLeast(nameof(rings), rings, 2);

        // A zero-length cylinder segment degenerates to a sphere.
        if (length <= 1e-5f)
            return CreateSphere(radius, segments, Math.Max(2, rings));

        var mesh = new MeshData();
        float half = length * 0.5f;
        float totalHeight = length + 2f * radius;

        // Each hemisphere adds 'rings' latitude rows; the middle cylinder adds a few more rows.
        int midRows = Math.Max(2, (int)MathF.Ceiling(length / radius));
        int rowSteps = 2 * rings + midRows;   // "latitude steps" between adjacent rows.

        // Outward normal reference for a triangle: sphere caps point toward the arc center, the middle points radially.
        Vector3 ArcOutward(Vector3 c)
        {
            if (c.Z >= half)
                return Vector3.Normalize(c - new Vector3(0f, 0f, half));
            if (c.Z <= -half)
                return Vector3.Normalize(c - new Vector3(0f, 0f, -half));
            return NormalizeXY(c);
        }

        // Arc length fraction from south pole to this z (used for UV v so the texture does not stretch).
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

            // Cross-section radius rho and the horizontal normal scale nxy at this height (closed forms).
            float rho, nxy;
            if (z >= half)
            {
                float d = MathF.Min(z - half, radius);
                rho = MathF.Sqrt(radius * radius - d * d);
                nxy = rho / radius;              // normal z component = d / radius
            }
            else if (z <= -half)
            {
                float d = MathF.Min(-(z + half), radius);
                rho = MathF.Sqrt(radius * radius - d * d);
                nxy = rho / radius;              // normal z component = -d / radius
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
            bool fromPole = k == 0;              // South pole sector
            bool toPole = k == rowSteps - 1;     // North pole sector

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
    // Cone (used by Arrow; axis along +Z, consistent with URDF/ROS)
    // =====================================================================

    /// <summary>
    /// Generates a cone: its revolution axis is +Z, the base (radius <paramref name="radius"/>) lies at
    /// z = -height/2 and the apex at +height/2. The base is capped and the side carries normals.
    /// </summary>
    internal static MeshData CreateCone(float radius, float height, int segments = 24)
    {
        EnsurePositive(nameof(radius), radius);
        EnsurePositive(nameof(height), height);
        EnsureAtLeast(nameof(segments), segments, 3);

        var mesh = new MeshData();
        float side = MathF.Atan2(radius, height);   // side inclination to the horizontal plane → normal
        float cosSide = MathF.Cos(side);
        float sinSide = MathF.Sin(side);

        // Base ring
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

        // Side (outward normal reference: the slanted normal of the current ring vertex)
        for (int i = 0; i < segments; i++)
        {
            uint next = ring[(i + 1) % segments];
            float ai = i / (float)segments * MathF.Tau;
            Vector3 outward = new Vector3(
                MathF.Cos(ai) * cosSide, MathF.Sin(ai) * cosSide, sinSide);
            AddOrientedTriangle(mesh, apex, next, ring[i], outward);
        }

        // Base cap (facing -Z)
        uint center = mesh.AddVertex(new Vector3(0f, 0f, -height * 0.5f), -Vector3.UnitZ, new Vector2(0.5f, 0.5f), Vector3.UnitX);
        for (int i = 0; i < segments; i++)
        {
            uint next = ring[(i + 1) % segments];
            AddOrientedTriangle(mesh, ring[i], next, center, -Vector3.UnitZ);
        }

        return mesh;
    }

    // =====================================================================
    // Plane
    // =====================================================================

    /// <summary>
    /// Generates a horizontal ground on the XY plane (z = 0) with normal +Z, width <paramref name="width"/>
    /// (along X) and depth <paramref name="depth"/> (along Y). For a Z-up engine (matching the URDF/ROS
    /// world convention).
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
