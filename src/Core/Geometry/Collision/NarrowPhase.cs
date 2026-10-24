using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry.Collision;

/// <summary>
/// Pure narrow-phase collision tests. Primitives are tested in world space; meshes are tested in their
/// own local space with the other shape transformed into it. Contact details are exact for primitives
/// and approximate for mesh / box-box pairs, which is enough for collision queries and debug display.
/// </summary>
internal static class NarrowPhase
{
    private const float Epsilon = 1e-6f;

    public static bool TryGetContact(CollisionShape a, Matrix4x4 worldA,
        CollisionShape b, Matrix4x4 worldB, out ShapeContact contact)
    {
        contact = default;

        Bounds boundsA = a.GetLocalBounds().Transformed(worldA);
        Bounds boundsB = b.GetLocalBounds().Transformed(worldB);
        if (!boundsA.Intersects(boundsB))
            return false;

        if (a is MeshShape meshA && b is MeshShape meshB)
            return MeshMesh(meshA, worldA, meshB, worldB, out contact);

        if (a is MeshShape firstMesh)
            return MeshShapeContact(firstMesh, worldA, b, worldB, out contact);

        if (b is MeshShape secondMesh)
        {
            if (!MeshShapeContact(secondMesh, worldB, a, worldA, out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        return PrimitiveContact(a, worldA, b, worldB, out contact);
    }

    private static bool PrimitiveContact(CollisionShape a, Matrix4x4 worldA,
        CollisionShape b, Matrix4x4 worldB, out ShapeContact contact)
    {
        contact = default;

        if (a is SphereShape sphereA && b is SphereShape sphereB)
            return SphereSphere(ToSphere(sphereA, worldA), ToSphere(sphereB, worldB), out contact);

        if (a is BoxShape boxA && b is BoxShape boxB)
            return BoxBox(ToBox(boxA, worldA), ToBox(boxB, worldB), out contact);

        if (a is BoxShape boxFirst && b is SphereShape sphereSecond)
            return BoxSphere(ToBox(boxFirst, worldA), ToSphere(sphereSecond, worldB), out contact);

        if (a is SphereShape sphereFirst && b is BoxShape boxSecond)
        {
            if (!BoxSphere(ToBox(boxSecond, worldB), ToSphere(sphereFirst, worldA), out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        if (a is CapsuleShape capsuleA && b is CapsuleShape capsuleB)
            return CapsuleCapsule(ToCapsule(capsuleA, worldA), ToCapsule(capsuleB, worldB), out contact);

        if (a is CapsuleShape capsuleFirst && b is SphereShape sphereThird)
            return CapsuleSphere(ToCapsule(capsuleFirst, worldA), ToSphere(sphereThird, worldB), out contact);

        if (a is SphereShape sphereFourth && b is CapsuleShape capsuleSecond)
        {
            if (!CapsuleSphere(ToCapsule(capsuleSecond, worldB), ToSphere(sphereFourth, worldA), out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        if (a is CylinderShape cylinderA && b is CapsuleShape capsuleC)
            return CapsuleCapsule(ToCapsule(cylinderA, worldA), ToCapsule(capsuleC, worldB), out contact);

        if (a is CapsuleShape capsuleD && b is CylinderShape cylinderB)
            return CapsuleCapsule(ToCapsule(capsuleD, worldA), ToCapsule(cylinderB, worldB), out contact);

        if (a is CylinderShape cylinderC && b is SphereShape sphereFifth)
            return CapsuleSphere(ToCapsule(cylinderC, worldA), ToSphere(sphereFifth, worldB), out contact);

        if (a is SphereShape sphereSixth && b is CylinderShape cylinderD)
        {
            if (!CapsuleSphere(ToCapsule(cylinderD, worldB), ToSphere(sphereSixth, worldA), out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        if (a is CylinderShape cylinderE && b is CylinderShape cylinderF)
            return CapsuleCapsule(ToCapsule(cylinderE, worldA), ToCapsule(cylinderF, worldB), out contact);

        if (a is CapsuleShape capsuleG && b is BoxShape boxFourth)
            return CapsuleBox(ToCapsule(capsuleG, worldA), ToBox(boxFourth, worldB), out contact);

        if (a is BoxShape boxFifth && b is CapsuleShape capsuleH)
        {
            if (!CapsuleBox(ToCapsule(capsuleH, worldB), ToBox(boxFifth, worldA), out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        if (a is CylinderShape cylinderG && b is BoxShape boxSixth)
            return CapsuleBox(ToCapsule(cylinderG, worldA), ToBox(boxSixth, worldB), out contact);

        if (a is BoxShape boxSeventh && b is CylinderShape cylinderH)
        {
            if (!CapsuleBox(ToCapsule(cylinderH, worldB), ToBox(boxSeventh, worldA), out ShapeContact reversed))
                return false;
            contact = new ShapeContact(reversed.Point, -reversed.Normal, reversed.Depth);
            return true;
        }

        return false;
    }

    private static bool SphereSphere(in SphereWorld a, in SphereWorld b, out ShapeContact contact)
    {
        contact = default;
        Vector3 delta = b.Center - a.Center;
        float distanceSquared = delta.LengthSquared();
        float radiusSum = a.Radius + b.Radius;
        if (distanceSquared > radiusSum * radiusSum)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        Vector3 normal = distance > Epsilon ? delta / distance : Vector3.UnitZ;
        float depth = radiusSum - distance;
        Vector3 point = a.Center + normal * (a.Radius - depth * 0.5f);
        contact = new ShapeContact(point, normal, MathF.Max(0f, depth));
        return true;
    }

    private static bool BoxSphere(in BoxWorld box, in SphereWorld sphere, out ShapeContact contact)
    {
        contact = default;

        Vector3 local = Rotate(sphere.Center - box.Center, Quaternion.Inverse(box.Rotation));
        Vector3 closest = Vector3.Clamp(local, -box.HalfExtents, box.HalfExtents);
        Vector3 delta = local - closest;
        float distanceSquared = delta.LengthSquared();
        if (distanceSquared > sphere.Radius * sphere.Radius)
            return false;

        Vector3 normalLocal;
        Vector3 pointLocal;
        float depth;
        if (distanceSquared > Epsilon)
        {
            float distance = MathF.Sqrt(distanceSquared);
            normalLocal = delta / distance;
            depth = sphere.Radius - distance;
            pointLocal = closest;
        }
        else
        {
            float dx = box.HalfExtents.X - MathF.Abs(local.X);
            float dy = box.HalfExtents.Y - MathF.Abs(local.Y);
            float dz = box.HalfExtents.Z - MathF.Abs(local.Z);

            if (dx <= dy && dx <= dz)
                normalLocal = new Vector3(local.X >= 0f ? 1f : -1f, 0f, 0f);
            else if (dy <= dz)
                normalLocal = new Vector3(0f, local.Y >= 0f ? 1f : -1f, 0f);
            else
                normalLocal = new Vector3(0f, 0f, local.Z >= 0f ? 1f : -1f);

            float faceDistance = normalLocal.X != 0f ? dx : normalLocal.Y != 0f ? dy : dz;
            depth = sphere.Radius + faceDistance;
            pointLocal = local + normalLocal * faceDistance;
        }

        Vector3 normal = Vector3.Normalize(Vector3.Transform(normalLocal, box.Rotation));
        Vector3 point = box.Center + Vector3.Transform(pointLocal, box.Rotation);
        contact = new ShapeContact(point, normal, MathF.Max(0f, depth));
        return true;
    }

    private static bool BoxBox(in BoxWorld a, in BoxWorld b, out ShapeContact contact)
    {
        contact = default;

        GetAxes(a.Rotation, out Vector3 ax, out Vector3 ay, out Vector3 az);
        GetAxes(b.Rotation, out Vector3 bx, out Vector3 by, out Vector3 bz);

        Span<Vector3> axes = stackalloc Vector3[15];
        axes[0] = ax;
        axes[1] = ay;
        axes[2] = az;
        axes[3] = bx;
        axes[4] = by;
        axes[5] = bz;
        int count = 6;
        AddCross(ref axes, ref count, ax, bx);
        AddCross(ref axes, ref count, ax, by);
        AddCross(ref axes, ref count, ax, bz);
        AddCross(ref axes, ref count, ay, bx);
        AddCross(ref axes, ref count, ay, by);
        AddCross(ref axes, ref count, ay, bz);
        AddCross(ref axes, ref count, az, bx);
        AddCross(ref axes, ref count, az, by);
        AddCross(ref axes, ref count, az, bz);

        float bestOverlap = float.MaxValue;
        Vector3 bestAxis = Vector3.UnitZ;

        for (int i = 0; i < count; i++)
        {
            Vector3 axis = axes[i];
            if (axis.LengthSquared() < 1e-8f)
                continue;
            axis = Vector3.Normalize(axis);

            float radiusA =
                MathF.Abs(Vector3.Dot(axis, ax)) * a.HalfExtents.X +
                MathF.Abs(Vector3.Dot(axis, ay)) * a.HalfExtents.Y +
                MathF.Abs(Vector3.Dot(axis, az)) * a.HalfExtents.Z;
            float radiusB =
                MathF.Abs(Vector3.Dot(axis, bx)) * b.HalfExtents.X +
                MathF.Abs(Vector3.Dot(axis, by)) * b.HalfExtents.Y +
                MathF.Abs(Vector3.Dot(axis, bz)) * b.HalfExtents.Z;

            float signedDistance = Vector3.Dot(b.Center - a.Center, axis);
            float overlap = radiusA + radiusB - MathF.Abs(signedDistance);
            if (overlap < 0f)
                return false;

            if (overlap < bestOverlap)
            {
                bestOverlap = overlap;
                bestAxis = signedDistance >= 0f ? axis : -axis;
            }
        }

        Vector3 supportA = a.Center +
                           ax * MathF.CopySign(a.HalfExtents.X, Vector3.Dot(bestAxis, ax)) +
                           ay * MathF.CopySign(a.HalfExtents.Y, Vector3.Dot(bestAxis, ay)) +
                           az * MathF.CopySign(a.HalfExtents.Z, Vector3.Dot(bestAxis, az));
        Vector3 supportB = b.Center +
                           bx * MathF.CopySign(b.HalfExtents.X, Vector3.Dot(-bestAxis, bx)) +
                           by * MathF.CopySign(b.HalfExtents.Y, Vector3.Dot(-bestAxis, by)) +
                           bz * MathF.CopySign(b.HalfExtents.Z, Vector3.Dot(-bestAxis, bz));

        contact = new ShapeContact((supportA + supportB) * 0.5f, bestAxis, MathF.Max(0f, bestOverlap));
        return true;
    }

    private static bool CapsuleCapsule(in CapsuleWorld a, in CapsuleWorld b, out ShapeContact contact)
    {
        contact = default;
        ClosestPointsSegmentSegment(a.A, a.B, b.A, b.B, out Vector3 pa, out Vector3 pb);
        Vector3 delta = pb - pa;
        float distanceSquared = delta.LengthSquared();
        float radiusSum = a.Radius + b.Radius;
        if (distanceSquared > radiusSum * radiusSum)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        Vector3 normal = distance > Epsilon ? delta / distance : Vector3.UnitZ;
        float depth = radiusSum - distance;
        Vector3 point = (pa + pb) * 0.5f;
        contact = new ShapeContact(point, normal, MathF.Max(0f, depth));
        return true;
    }

    private static bool CapsuleSphere(in CapsuleWorld capsule, in SphereWorld sphere, out ShapeContact contact)
    {
        contact = default;
        ClosestPointSegment(capsule.A, capsule.B, sphere.Center, out Vector3 closest);
        Vector3 delta = sphere.Center - closest;
        float distanceSquared = delta.LengthSquared();
        float radiusSum = capsule.Radius + sphere.Radius;
        if (distanceSquared > radiusSum * radiusSum)
            return false;

        float distance = MathF.Sqrt(distanceSquared);
        Vector3 normal = distance > Epsilon ? delta / distance : Vector3.UnitZ;
        float depth = radiusSum - distance;
        contact = new ShapeContact(closest + normal * capsule.Radius, normal, MathF.Max(0f, depth));
        return true;
    }

    private static bool CapsuleBox(in CapsuleWorld capsule, in BoxWorld box, out ShapeContact contact)
    {
        contact = default;

        Vector3 localA = Rotate(capsule.A - box.Center, Quaternion.Inverse(box.Rotation));
        Vector3 localB = Rotate(capsule.B - box.Center, Quaternion.Inverse(box.Rotation));

        Vector3 expanded = box.HalfExtents + new Vector3(capsule.Radius);
        if (!SegmentIntersectsAabb(localA, localB, -expanded, expanded))
            return false;

        const int samples = 24;
        float bestDistanceSquared = float.MaxValue;
        Vector3 bestPoint = localA;
        Vector3 bestClosest = Vector3.Clamp(localA, -box.HalfExtents, box.HalfExtents);
        for (int i = 0; i <= samples; i++)
        {
            Vector3 sample = Vector3.Lerp(localA, localB, i / (float)samples);
            Vector3 closest = Vector3.Clamp(sample, -box.HalfExtents, box.HalfExtents);
            float distanceSquared = Vector3.DistanceSquared(sample, closest);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestPoint = sample;
                bestClosest = closest;
            }
        }

        float distanceToBox = MathF.Sqrt(bestDistanceSquared);
        if (distanceToBox > capsule.Radius)
            return false;

        Vector3 delta = bestPoint - bestClosest;
        Vector3 normalLocal = delta.LengthSquared() > Epsilon ? Vector3.Normalize(delta) : Vector3.UnitZ;
        float depth = MathF.Max(0f, capsule.Radius - distanceToBox);

        Vector3 normal = Vector3.Normalize(Vector3.Transform(normalLocal, box.Rotation));
        Vector3 point = box.Center + Vector3.Transform(bestClosest, box.Rotation);
        contact = new ShapeContact(point, normal, depth);
        return true;
    }

    private static bool MeshMesh(MeshShape a, Matrix4x4 worldA, MeshShape b, Matrix4x4 worldB,
        out ShapeContact contact)
    {
        contact = default;
        TriangleBvh bvhA = a.GetBvh();
        TriangleBvh bvhB = b.GetBvh();
        if (bvhA.IsEmpty || bvhB.IsEmpty)
            return false;
        if (!Matrix4x4.Invert(worldA, out Matrix4x4 inverseA))
            return false;

        Matrix4x4 bToA = worldB * inverseA;
        return MeshMeshNode(bvhA, bvhB, bToA, 0, 0, worldA, worldB, out contact);
    }

    private static bool MeshMeshNode(TriangleBvh a, TriangleBvh b, in Matrix4x4 bToA,
        int nodeA, int nodeB, in Matrix4x4 worldA, in Matrix4x4 worldB, out ShapeContact contact)
    {
        contact = default;
        TriangleBvh.Node currentA = a.Nodes[nodeA];
        TriangleBvh.Node currentB = b.Nodes[nodeB];

        Bounds boundsB = currentB.Bounds.Transformed(bToA);
        if (!currentA.Bounds.Intersects(boundsB))
            return false;

        if (currentA.IsLeaf && currentB.IsLeaf)
        {
            for (int i = currentA.Start; i < currentA.Start + currentA.Count; i++)
            {
                a.GetTriangle(i, out Vector3 a0, out Vector3 a1, out Vector3 a2);
                for (int j = currentB.Start; j < currentB.Start + currentB.Count; j++)
                {
                    b.GetTriangle(j, out Vector3 b0, out Vector3 b1, out Vector3 b2);
                    if (TrianglesIntersect(a0, a1, a2,
                            Vector3.Transform(b0, bToA),
                            Vector3.Transform(b1, bToA),
                            Vector3.Transform(b2, bToA)))
                    {
                        Vector3 centroidA = (a0 + a1 + a2) / 3f;
                        Vector3 centroidB = (Vector3.Transform(b0, bToA) +
                                              Vector3.Transform(b1, bToA) +
                                              Vector3.Transform(b2, bToA)) / 3f;
                        Vector3 worldA0 = Vector3.Transform((a0 + a1 + a2) / 3f, worldA);
                        Vector3 worldB0 = Vector3.Transform((b0 + b1 + b2) / 3f, worldB);
                        Vector3 direction = worldB0 - worldA0;
                        Vector3 normal = direction.LengthSquared() > Epsilon
                            ? Vector3.Normalize(direction)
                            : Vector3.Normalize(Vector3.Cross(Vector3.Transform(b1 - b0, worldB),
                                Vector3.Transform(b2 - b0, worldB)));
                        contact = new ShapeContact(Vector3.Transform((centroidA + centroidB) * 0.5f, worldA),
                            normal, 0f);
                        return true;
                    }
                }
            }

            return false;
        }

        if (currentA.IsLeaf || (!currentB.IsLeaf && currentB.Bounds.Size.LengthSquared() > currentA.Bounds.Size.LengthSquared()))
        {
            return MeshMeshNode(a, b, bToA, nodeA, currentB.Left, worldA, worldB, out contact) ||
                   MeshMeshNode(a, b, bToA, nodeA, currentB.Right, worldA, worldB, out contact);
        }

        return MeshMeshNode(a, b, bToA, currentA.Left, nodeB, worldA, worldB, out contact) ||
               MeshMeshNode(a, b, bToA, currentA.Right, nodeB, worldA, worldB, out contact);
    }

    private static bool MeshShapeContact(MeshShape mesh, Matrix4x4 worldMesh,
        CollisionShape shape, Matrix4x4 worldShape, out ShapeContact contact)
    {
        contact = default;
        TriangleBvh bvh = mesh.GetBvh();
        if (bvh.IsEmpty)
            return false;
        if (!Matrix4x4.Invert(worldMesh, out Matrix4x4 inverseMesh))
            return false;

        Matrix4x4 shapeToMesh = worldShape * inverseMesh;
        Bounds shapeBounds = shape.GetLocalBounds().Transformed(shapeToMesh);
        return MeshShapeNode(bvh, shape, shapeToMesh, shapeBounds, 0, worldMesh, worldShape, out contact);
    }

    private static bool MeshShapeNode(TriangleBvh mesh, CollisionShape shape, in Matrix4x4 shapeToMesh,
        in Bounds shapeBounds, int nodeIndex, in Matrix4x4 worldMesh, in Matrix4x4 worldShape,
        out ShapeContact contact)
    {
        contact = default;
        TriangleBvh.Node node = mesh.Nodes[nodeIndex];
        if (!node.Bounds.Intersects(shapeBounds))
            return false;

        if (node.IsLeaf)
        {
            for (int i = node.Start; i < node.Start + node.Count; i++)
            {
                mesh.GetTriangle(i, out Vector3 a, out Vector3 b, out Vector3 c);
                if (!TriangleShapeIntersects(a, b, c, shape, shapeToMesh))
                    continue;

                Vector3 triangleCenter = Vector3.Transform((a + b + c) / 3f, worldMesh);
                Vector3 shapeCenter = GetWorldCenter(shape, worldShape);
                Vector3 direction = shapeCenter - triangleCenter;
                Vector3 normal = direction.LengthSquared() > Epsilon
                    ? Vector3.Normalize(direction)
                    : Vector3.UnitZ;
                contact = new ShapeContact(triangleCenter, normal, 0f);
                return true;
            }

            return false;
        }

        return MeshShapeNode(mesh, shape, shapeToMesh, shapeBounds, node.Left, worldMesh, worldShape, out contact) ||
               MeshShapeNode(mesh, shape, shapeToMesh, shapeBounds, node.Right, worldMesh, worldShape, out contact);
    }

    private static bool TriangleShapeIntersects(Vector3 a, Vector3 b, Vector3 c,
        CollisionShape shape, in Matrix4x4 shapeToMesh)
    {
        switch (shape)
        {
            case SphereShape sphere:
            {
                Vector3 center = Vector3.Transform(Vector3.Zero, shapeToMesh);
                Vector3 closest = ClosestPointOnTriangle(center, a, b, c);
                float radius = sphere.Radius * MaxScale(shapeToMesh);
                return Vector3.DistanceSquared(center, closest) <= radius * radius;
            }

            case BoxShape box:
            {
                Matrix4x4 meshToBox = InvertOrIdentity(shapeToMesh);
                Vector3 ta = Vector3.Transform(a, meshToBox);
                Vector3 tb = Vector3.Transform(b, meshToBox);
                Vector3 tc = Vector3.Transform(c, meshToBox);
                Vector3 half = box.Size * 0.5f;
                return TriangleAabbIntersects(ta, tb, tc, -half, half);
            }

            case CapsuleShape capsule:
            {
                Vector3 localA = Vector3.Transform(Vector3.UnitZ * (-capsule.Length * 0.5f), shapeToMesh);
                Vector3 localB = Vector3.Transform(Vector3.UnitZ * (capsule.Length * 0.5f), shapeToMesh);
                float radius = capsule.Radius * MaxScale(shapeToMesh);
                return SegmentTriangleDistanceSquared(localA, localB, a, b, c) <= radius * radius;
            }

            case CylinderShape cylinder:
            {
                Vector3 localA = Vector3.Transform(Vector3.UnitZ * (-cylinder.Length * 0.5f), shapeToMesh);
                Vector3 localB = Vector3.Transform(Vector3.UnitZ * (cylinder.Length * 0.5f), shapeToMesh);
                float radius = cylinder.Radius * MaxScale(shapeToMesh);
                return SegmentTriangleDistanceSquared(localA, localB, a, b, c) <= radius * radius;
            }

            default:
                return false;
        }
    }

    private static Vector3 GetWorldCenter(CollisionShape shape, in Matrix4x4 world)
    {
        switch (shape)
        {
            case BoxShape:
            case SphereShape:
            case CapsuleShape:
            case CylinderShape:
                return Vector3.Transform(Vector3.Zero, world);
            case MeshShape mesh:
                return Vector3.Transform(mesh.LocalBounds.Center, world);
            default:
                return Vector3.Transform(Vector3.Zero, world);
        }
    }

    private static bool TrianglesIntersect(Vector3 a0, Vector3 a1, Vector3 a2,
        Vector3 b0, Vector3 b1, Vector3 b2)
    {
        Vector3 nA = Vector3.Cross(a1 - a0, a2 - a0);
        Vector3 nB = Vector3.Cross(b1 - b0, b2 - b0);

        Span<Vector3> axes = stackalloc Vector3[17];
        axes[0] = nA;
        axes[1] = nB;
        int count = 2;
        Vector3[] edgesA = { a1 - a0, a2 - a1, a0 - a2 };
        Vector3[] edgesB = { b1 - b0, b2 - b1, b0 - b2 };
        for (int i = 0; i < 3; i++)
        {
            AddCross(ref axes, ref count, nA, edgesA[i]);
            AddCross(ref axes, ref count, nB, edgesB[i]);
            for (int j = 0; j < 3; j++)
                AddCross(ref axes, ref count, edgesA[i], edgesB[j]);
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 axis = axes[i];
            if (axis.LengthSquared() < 1e-8f)
                continue;
            axis = Vector3.Normalize(axis);
            Project(a0, a1, a2, axis, out float minA, out float maxA);
            Project(b0, b1, b2, axis, out float minB, out float maxB);
            if (maxA < minB - Epsilon || maxB < minA - Epsilon)
                return false;
        }

        return true;
    }

    private static bool TriangleAabbIntersects(Vector3 a, Vector3 b, Vector3 c, Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;

        Vector3 n = Vector3.Cross(b - a, c - a);
        Span<Vector3> axes = stackalloc Vector3[13];
        axes[0] = n;
        axes[1] = Vector3.UnitX;
        axes[2] = Vector3.UnitY;
        axes[3] = Vector3.UnitZ;
        int count = 4;
        Vector3[] edges = { b - a, c - b, a - c };
        for (int i = 0; i < 3; i++)
        {
            AddCross(ref axes, ref count, edges[i], Vector3.UnitX);
            AddCross(ref axes, ref count, edges[i], Vector3.UnitY);
            AddCross(ref axes, ref count, edges[i], Vector3.UnitZ);
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 axis = axes[i];
            if (axis.LengthSquared() < 1e-8f)
                continue;
            axis = Vector3.Normalize(axis);
            Project(a, b, c, axis, out float minT, out float maxT);
            float radius = half.X * MathF.Abs(axis.X) + half.Y * MathF.Abs(axis.Y) + half.Z * MathF.Abs(axis.Z);
            float projectedCenter = Vector3.Dot(center, axis);
            if (maxT < projectedCenter - radius - Epsilon || minT > projectedCenter + radius + Epsilon)
                return false;
        }

        return true;
    }

    private static void Project(Vector3 a, Vector3 b, Vector3 c, Vector3 axis, out float min, out float max)
    {
        float pa = Vector3.Dot(a, axis);
        float pb = Vector3.Dot(b, axis);
        float pc = Vector3.Dot(c, axis);
        min = MathF.Min(pa, MathF.Min(pb, pc));
        max = MathF.Max(pa, MathF.Max(pb, pc));
    }

    private static void AddCross(ref Span<Vector3> axes, ref int count, Vector3 a, Vector3 b)
    {
        if (count >= axes.Length)
            return;
        axes[count++] = Vector3.Cross(a, b);
    }

    private static void GetAxes(Quaternion rotation, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        x = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, rotation));
        y = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
        z = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, rotation));
    }

    private static Vector3 Rotate(Vector3 value, Quaternion rotation)
        => Vector3.Transform(value, rotation);

    private static SphereWorld ToSphere(SphereShape shape, in Matrix4x4 world)
    {
        Decompose(world, out Vector3 center, out _, out Vector3 scale);
        return new SphereWorld(center, shape.Radius * MaxComponent(scale));
    }

    private static BoxWorld ToBox(BoxShape shape, in Matrix4x4 world)
    {
        Decompose(world, out Vector3 center, out Quaternion rotation, out Vector3 scale);
        return new BoxWorld(center, rotation, shape.Size * 0.5f * scale);
    }

    private static CapsuleWorld ToCapsule(CapsuleShape shape, in Matrix4x4 world)
    {
        Decompose(world, out Vector3 center, out Quaternion rotation, out Vector3 scale);
        float halfLength = shape.Length * 0.5f * scale.Z;
        Vector3 axis = Vector3.Transform(Vector3.UnitZ, rotation);
        return new CapsuleWorld(center - axis * halfLength, center + axis * halfLength,
            shape.Radius * MathF.Max(scale.X, scale.Y));
    }

    private static CapsuleWorld ToCapsule(CylinderShape shape, in Matrix4x4 world)
    {
        Decompose(world, out Vector3 center, out Quaternion rotation, out Vector3 scale);
        float halfLength = shape.Length * 0.5f * scale.Z;
        Vector3 axis = Vector3.Transform(Vector3.UnitZ, rotation);
        return new CapsuleWorld(center - axis * halfLength, center + axis * halfLength,
            shape.Radius * MathF.Max(scale.X, scale.Y));
    }

    private static void Decompose(in Matrix4x4 world, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        if (!Matrix4x4.Decompose(world, out scale, out rotation, out position))
        {
            position = world.Translation;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
        }

        if (rotation.LengthSquared() < 1e-12f)
            rotation = Quaternion.Identity;
        else
            rotation = Quaternion.Normalize(rotation);

        scale = new Vector3(MathF.Abs(scale.X), MathF.Abs(scale.Y), MathF.Abs(scale.Z));
        if (scale.X < 1e-6f) scale.X = 1e-6f;
        if (scale.Y < 1e-6f) scale.Y = 1e-6f;
        if (scale.Z < 1e-6f) scale.Z = 1e-6f;
    }

    private static float MaxComponent(Vector3 value)
        => MathF.Max(value.X, MathF.Max(value.Y, value.Z));

    private static float MaxScale(in Matrix4x4 matrix)
    {
        float x = new Vector3(matrix.M11, matrix.M12, matrix.M13).Length();
        float y = new Vector3(matrix.M21, matrix.M22, matrix.M23).Length();
        float z = new Vector3(matrix.M31, matrix.M32, matrix.M33).Length();
        return MathF.Max(1e-6f, MathF.Max(x, MathF.Max(y, z)));
    }

    private static Matrix4x4 InvertOrIdentity(in Matrix4x4 matrix)
        => Matrix4x4.Invert(matrix, out Matrix4x4 inverse) ? inverse : Matrix4x4.Identity;

    private static bool SegmentIntersectsAabb(Vector3 start, Vector3 end, Vector3 min, Vector3 max)
    {
        float tMin = 0f, tMax = 1f;
        Vector3 direction = end - start;
        return Slab(start.X, direction.X, min.X, max.X, ref tMin, ref tMax)
               && Slab(start.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax)
               && Slab(start.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax);
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(direction) < 1e-9f)
            return origin >= min && origin <= max;

        float t1 = (min - origin) / direction;
        float t2 = (max - origin) / direction;
        if (t1 > t2)
            (t1, t2) = (t2, t1);
        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;
        return tMin <= tMax;
    }

    private static void ClosestPointSegment(Vector3 a, Vector3 b, Vector3 point, out Vector3 closest)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-12f)
        {
            closest = a;
            return;
        }

        float t = Math.Clamp(Vector3.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        closest = a + ab * t;
    }

    private static void ClosestPointsSegmentSegment(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1,
        out Vector3 p, out Vector3 q)
    {
        Vector3 d1 = p1 - p0;
        Vector3 d2 = q1 - q0;
        Vector3 r = p0 - q0;
        float a = d1.LengthSquared();
        float e = d2.LengthSquared();
        float f = Vector3.Dot(d2, r);
        float s, t;

        if (a <= Epsilon && e <= Epsilon)
        {
            s = t = 0f;
        }
        else if (a <= Epsilon)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = denominator != 0f ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        p = p0 + d1 * s;
        q = q0 + d2 * t;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
            return a;

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
            return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + ab * v;
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
            return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + ac * w;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + (c - b) * w;
        }

        float denominator = va + vb + vc;
        if (MathF.Abs(denominator) < Epsilon)
            return a;

        float v0 = vb / denominator;
        float w0 = vc / denominator;
        return a + ab * v0 + ac * w0;
    }

    private static float SegmentTriangleDistanceSquared(Vector3 segmentA, Vector3 segmentB,
        Vector3 a, Vector3 b, Vector3 c)
    {
        if (SegmentIntersectsTriangle(segmentA, segmentB, a, b, c))
            return 0f;

        float best = MathF.Min(
            PointSegmentDistanceSquared(a, segmentA, segmentB),
            MathF.Min(PointSegmentDistanceSquared(b, segmentA, segmentB),
                PointSegmentDistanceSquared(c, segmentA, segmentB)));

        best = MathF.Min(best, PointTriangleDistanceSquared(segmentA, a, b, c));
        best = MathF.Min(best, PointTriangleDistanceSquared(segmentB, a, b, c));

        best = MathF.Min(best, SegmentSegmentDistanceSquared(segmentA, segmentB, a, b));
        best = MathF.Min(best, SegmentSegmentDistanceSquared(segmentA, segmentB, b, c));
        best = MathF.Min(best, SegmentSegmentDistanceSquared(segmentA, segmentB, c, a));
        return best;
    }

    private static float PointTriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 closest = ClosestPointOnTriangle(point, a, b, c);
        return Vector3.DistanceSquared(point, closest);
    }

    private static float PointSegmentDistanceSquared(Vector3 point, Vector3 a, Vector3 b)
    {
        ClosestPointSegment(a, b, point, out Vector3 closest);
        return Vector3.DistanceSquared(point, closest);
    }

    private static float SegmentSegmentDistanceSquared(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        ClosestPointsSegmentSegment(a0, a1, b0, b1, out Vector3 p, out Vector3 q);
        return Vector3.DistanceSquared(p, q);
    }

    private static bool SegmentIntersectsTriangle(Vector3 segmentA, Vector3 segmentB,
        Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 direction = segmentB - segmentA;
        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 normal = Vector3.Cross(e1, e2);
        float denominator = Vector3.Dot(normal, direction);
        if (MathF.Abs(denominator) < Epsilon)
            return false;

        float t = Vector3.Dot(a - segmentA, normal) / denominator;
        if (t < 0f || t > 1f)
            return false;

        Vector3 point = segmentA + direction * t;
        Vector3 c0 = Vector3.Cross(b - a, point - a);
        Vector3 c1 = Vector3.Cross(c - b, point - b);
        Vector3 c2 = Vector3.Cross(a - c, point - c);
        return Vector3.Dot(normal, c0) >= -Epsilon
               && Vector3.Dot(normal, c1) >= -Epsilon
               && Vector3.Dot(normal, c2) >= -Epsilon;
    }

    private readonly record struct SphereWorld(Vector3 Center, float Radius);

    private readonly record struct BoxWorld(Vector3 Center, Quaternion Rotation, Vector3 HalfExtents);

    private readonly record struct CapsuleWorld(Vector3 A, Vector3 B, float Radius);
}
