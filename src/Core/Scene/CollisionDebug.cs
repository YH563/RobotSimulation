using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Builds Godot-style debug volumes for collision shapes: a muted low-opacity unlit fill plus a soft wireframe
/// outline. Both use <see cref="RenderPassKind.Debug"/>, which the OpenGL backend draws after the opaque scene
/// with alpha blending and no depth test. Mesh shapes draw their own geometry and skip the outline (their mesh
/// is often dense); primitives get a clean box / circle outline.
/// </summary>
public static class CollisionDebug
{
    /// <summary>Default fill color: a muted, low-opacity blue-grey, close to a physics-debug overlay.</summary>
    public static readonly Vector4 DefaultColor = new(0.28f, 0.56f, 0.72f, 0.16f);

    /// <summary>Default outline color: a soft blue-grey edge, strong enough to read but not to glare.</summary>
    public static readonly Vector4 DefaultOutlineColor = new(0.40f, 0.68f, 0.82f, 0.45f);

    /// <summary>
    /// Creates a world-space translucent fill for one shape instance. The returned node's transform already
    /// includes <paramref name="worldPose"/>; add it to the scene as a root.
    /// </summary>
    /// <param name="instance">Shape and its pose relative to the owner.</param>
    /// <param name="worldPose">Owner's world matrix.</param>
    /// <param name="color">RGBA fill color; null uses <see cref="DefaultColor"/>.</param>
    /// <param name="name">Scene node name; null derives one from the shape.</param>
    /// <param name="outlineColor">Outline color; null uses <see cref="DefaultOutlineColor"/>.</param>
    public static GameObject CreateFill(CollisionShapeInstance instance, Matrix4x4 worldPose,
        Vector4? color = null, string? name = null, Vector4? outlineColor = null)
        => CreateFillNode(instance, instance.LocalPose * worldPose, color, name, outlineColor);

    /// <summary>
    /// Creates a fill in the owner's local space. Its transform is <see cref="CollisionShapeInstance.LocalPose"/>,
    /// so the caller can parent it under the owner's node and let it follow joints automatically.
    /// </summary>
    /// <param name="instance">Shape and its pose relative to the owner.</param>
    /// <param name="color">RGBA fill color; null uses <see cref="DefaultColor"/>.</param>
    /// <param name="name">Scene node name; null derives one from the shape.</param>
    /// <param name="outlineColor">Outline color; null uses <see cref="DefaultOutlineColor"/>.</param>
    public static GameObject CreateLocalFill(CollisionShapeInstance instance, Vector4? color = null,
        string? name = null, Vector4? outlineColor = null)
        => CreateFillNode(instance, instance.LocalPose, color, name, outlineColor);

    /// <summary>
    /// Creates a short line marker from a contact point along its normal. The node uses the Line pass and is
    /// not pickable.
    /// </summary>
    /// <param name="contact">Contact to mark.</param>
    /// <param name="normalLength">Length of the marker in world units.</param>
    /// <param name="color">Line color; null uses a bright red.</param>
    public static GameObject CreateContactMarker(in CollisionContact contact, float normalLength = 0.12f,
        Vector4? color = null)
    {
        Vector3 end = contact.Point + contact.Normal * normalLength;
        var marker = new Curve(new[] { contact.Point, end },
            color ?? new Vector4(1f, 0.15f, 0.1f, 1f), "collision-contact", lineWidth: 3f)
        {
            Pickable = false,
        };
        return marker;
    }

    private static GameObject CreateFillNode(CollisionShapeInstance instance, Matrix4x4 localMatrix,
        Vector4? color, string? name, Vector4? outlineColor)
    {
        if (instance.Shape is null)
            throw new ArgumentNullException(nameof(instance));

        MeshData mesh;
        Matrix4x4 matrix = localMatrix;
        string shapeName;

        switch (instance.Shape)
        {
            case BoxShape box:
                mesh = Primitives.CreateBox(box.Size.X, box.Size.Y, box.Size.Z);
                shapeName = "collision-box";
                break;

            case SphereShape sphere:
                mesh = Primitives.CreateSphere(sphere.Radius);
                shapeName = "collision-sphere";
                break;

            case CapsuleShape capsule:
                mesh = Primitives.CreateCapsule(capsule.Radius, capsule.Length);
                shapeName = "collision-capsule";
                break;

            case CylinderShape cylinder:
                mesh = Primitives.CreateCylinder(cylinder.Radius, cylinder.Length);
                shapeName = "collision-cylinder";
                break;

            case MeshShape meshShape:
                mesh = meshShape.Mesh;
                shapeName = "collision-mesh";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(instance), instance.Shape, "Unsupported collision shape.");
        }

        var material = new MaterialData
        {
            PassKind = RenderPassKind.Debug,
            BaseColor = color ?? DefaultColor,
            DoubleSided = true,
        };

        var node = new GameObject(mesh, material, name ?? shapeName)
        {
            Pickable = false,
        };
        ApplyMatrix(node.Transform, matrix);

        if (BuildOutline(instance.Shape) is { } outline)
        {
            var outlineNode = new GameObject(null, new MaterialData
            {
                PassKind = RenderPassKind.Debug,
                BaseColor = outlineColor ?? DefaultOutlineColor,
            }, $"{node.Name}:outline")
            {
                LineData = outline,
                LineWidth = 1.25f,
                Pickable = false,
            };
            outlineNode.Transform.Parent = node.Transform;
        }

        return node;
    }

    private static LineData? BuildOutline(CollisionShape shape)
    {
        switch (shape)
        {
            case BoxShape box:
                return BoxOutline(box.Size * 0.5f);

            case SphereShape sphere:
                return SphereOutline(sphere.Radius);

            case CapsuleShape capsule:
                return capsule.Length <= 1e-5f
                    ? SphereOutline(capsule.Radius)
                    : CapsuleOutline(capsule.Radius, capsule.Length);

            case CylinderShape cylinder:
                return CylinderOutline(cylinder.Radius, cylinder.Length);

            default:
                return null;
        }
    }

    private static LineData BoxOutline(Vector3 half)
    {
        var lines = new LineData();
        Vector3[] corners =
        {
            new(-half.X, -half.Y, -half.Z),
            new(half.X, -half.Y, -half.Z),
            new(half.X, half.Y, -half.Z),
            new(-half.X, half.Y, -half.Z),
            new(-half.X, -half.Y, half.Z),
            new(half.X, -half.Y, half.Z),
            new(half.X, half.Y, half.Z),
            new(-half.X, half.Y, half.Z),
        };
        int[] edges =
        {
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        };
        for (int i = 0; i < edges.Length; i += 2)
            lines.AddSegment(corners[edges[i]], corners[edges[i + 1]]);
        return lines;
    }

    private static LineData SphereOutline(float radius)
    {
        var lines = new LineData();
        AddCircle(lines, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, radius);
        AddCircle(lines, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, radius);
        AddCircle(lines, Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, radius);
        return lines;
    }

    private static LineData CylinderOutline(float radius, float length)
    {
        var lines = new LineData();
        float half = length * 0.5f;
        AddCircle(lines, new Vector3(0f, 0f, half), Vector3.UnitX, Vector3.UnitY, radius);
        AddCircle(lines, new Vector3(0f, 0f, -half), Vector3.UnitX, Vector3.UnitY, radius);
        AddSideLines(lines, radius, half);
        return lines;
    }

    private static LineData CapsuleOutline(float radius, float length)
    {
        var lines = new LineData();
        float half = length * 0.5f;
        AddCircle(lines, new Vector3(0f, 0f, half), Vector3.UnitX, Vector3.UnitY, radius);
        AddCircle(lines, new Vector3(0f, 0f, -half), Vector3.UnitX, Vector3.UnitY, radius);
        AddSideLines(lines, radius, half);
        return lines;
    }

    private static void AddSideLines(LineData lines, float radius, float half)
    {
        lines.AddSegment(new Vector3(radius, 0f, -half), new Vector3(radius, 0f, half));
        lines.AddSegment(new Vector3(-radius, 0f, -half), new Vector3(-radius, 0f, half));
        lines.AddSegment(new Vector3(0f, radius, -half), new Vector3(0f, radius, half));
        lines.AddSegment(new Vector3(0f, -radius, -half), new Vector3(0f, -radius, half));
    }

    private static void AddCircle(LineData lines, Vector3 center, Vector3 axisA, Vector3 axisB,
        float radius, int segments = 32)
    {
        Vector3 previous = center + axisA * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = MathF.Tau * i / segments;
            Vector3 point = center + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;
            lines.AddSegment(previous, point);
            previous = point;
        }
    }

    private static void ApplyMatrix(Transform transform, in Matrix4x4 matrix)
    {
        if (Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 position))
            transform.SetLocalPose(position, rotation, scale);
        else
            transform.SetLocalPose(matrix.Translation, Quaternion.Identity, Vector3.One);
    }
}
