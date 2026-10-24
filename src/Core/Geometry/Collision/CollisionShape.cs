using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry.Collision;

/// <summary>
/// A collision shape in its own local space. Shapes hold parameters only; the pose comes from a
/// <see cref="CollisionShapeInstance"/>. Units are meters and the primitive revolution axis is local +Z,
/// matching URDF / ROS semantics.
/// </summary>
public abstract class CollisionShape
{
    internal abstract Bounds GetLocalBounds();
}

/// <summary>Axis-aligned box in local space. <see cref="Size"/> is the full extent on x/y/z (URDF &lt;box&gt;).</summary>
public sealed class BoxShape : CollisionShape
{
    /// <summary>Full width on x/y/z.</summary>
    public Vector3 Size { get; }

    /// <summary>Creates a box shape.</summary>
    /// <param name="size">Full extent on x/y/z; every component must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A component is not a positive finite number.</exception>
    public BoxShape(Vector3 size)
    {
        EnsurePositive(size.X, nameof(size));
        EnsurePositive(size.Y, nameof(size));
        EnsurePositive(size.Z, nameof(size));
        Size = size;
    }

    internal override Bounds GetLocalBounds()
    {
        Vector3 half = Size * 0.5f;
        return new Bounds(-half, half);
    }

    private static void EnsurePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(name, value, "Box size components must be positive finite values.");
    }
}

/// <summary>Sphere in local space.</summary>
public sealed class SphereShape : CollisionShape
{
    /// <summary>Radius in meters.</summary>
    public float Radius { get; }

    /// <summary>Creates a sphere shape.</summary>
    /// <param name="radius">Radius in meters; must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not a positive finite number.</exception>
    public SphereShape(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Sphere radius must be a positive finite value.");
        Radius = radius;
    }

    internal override Bounds GetLocalBounds()
    {
        var radius = new Vector3(Radius);
        return new Bounds(-radius, radius);
    }
}

/// <summary>Capsule in local space, axis along +Z. <see cref="Length"/> is the middle cylinder segment (excluding caps).</summary>
public sealed class CapsuleShape : CollisionShape
{
    /// <summary>Radius in meters, including the hemispherical caps.</summary>
    public float Radius { get; }

    /// <summary>Middle cylinder segment length in meters (total height = Length + 2 × Radius).</summary>
    public float Length { get; }

    /// <summary>Creates a capsule shape.</summary>
    /// <param name="radius">Radius in meters; must be positive and finite.</param>
    /// <param name="length">Middle segment length; must be non-negative and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public CapsuleShape(float radius, float length)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Capsule radius must be a positive finite value.");
        if (!float.IsFinite(length) || length < 0f)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Capsule length must be a non-negative finite value.");
        Radius = radius;
        Length = length;
    }

    internal override Bounds GetLocalBounds()
    {
        float halfLength = Length * 0.5f + Radius;
        return new Bounds(new Vector3(-Radius, -Radius, -halfLength), new Vector3(Radius, Radius, halfLength));
    }
}

/// <summary>
/// Cylinder in local space, axis along +Z. <see cref="Length"/> is the full height.
/// Narrow-phase queries treat it as a capsule in this first version; the debug view still draws a cylinder.
/// </summary>
public sealed class CylinderShape : CollisionShape
{
    /// <summary>Radius in meters.</summary>
    public float Radius { get; }

    /// <summary>Full height in meters.</summary>
    public float Length { get; }

    /// <summary>Creates a cylinder shape.</summary>
    /// <param name="radius">Radius in meters; must be positive and finite.</param>
    /// <param name="length">Full height; must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public CylinderShape(float radius, float length)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Cylinder radius must be a positive finite value.");
        if (!float.IsFinite(length) || length <= 0f)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Cylinder length must be a positive finite value.");
        Radius = radius;
        Length = length;
    }

    internal override Bounds GetLocalBounds()
    {
        float half = Length * 0.5f;
        return new Bounds(new Vector3(-Radius, -Radius, -half), new Vector3(Radius, Radius, half));
    }
}

/// <summary>
/// Triangle mesh in local space. The shape caches a triangle BVH and rebuilds it lazily when
/// <see cref="MeshData.Revision"/> changes, so an incrementally reconstructed mesh can be used directly.
/// Reads are thread-safe; do not mutate the mesh while another thread is querying it.
/// </summary>
public sealed class MeshShape : CollisionShape
{
    private readonly object _gate = new();
    private TriangleBvh? _bvh;
    private long _bvhRevision = -1;

    /// <summary>Creates a mesh collision shape wrapping <paramref name="mesh"/>.</summary>
    /// <param name="mesh">Triangle mesh; it is referenced, not copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    public MeshShape(MeshData mesh)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
    }

    /// <summary>The wrapped mesh.</summary>
    public MeshData Mesh { get; }

    /// <summary>Local-space bounds of the current mesh revision (computed lazily).</summary>
    public Bounds LocalBounds => GetBvh().Bounds;

    internal override Bounds GetLocalBounds() => GetBvh().Bounds;

    internal TriangleBvh GetBvh()
    {
        lock (_gate)
        {
            if (_bvh is null || _bvhRevision != Mesh.Revision)
            {
                _bvh = TriangleBvh.Build(Mesh);
                _bvhRevision = Mesh.Revision;
            }

            return _bvh;
        }
    }
}
