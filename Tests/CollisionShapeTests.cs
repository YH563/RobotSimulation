using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Tests;

/// <summary>
/// Primitive collision queries without a GL context: face/edge cases, contact orientation, and the
/// scene-level AABB broad phase. Mesh collision is covered by <see cref="CollisionMeshTests"/>.
/// </summary>
public class CollisionShapeTests
{
    private static readonly Matrix4x4 Identity = Matrix4x4.Identity;

    [Fact]
    public void SpheresOverlapWhenTheirRadiiMeet()
    {
        var a = new SphereShape(1f);
        var b = new SphereShape(1f);

        Assert.True(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(1.5f, 0f, 0f), out ShapeContact contact));
        Assert.True(contact.Depth > 0f);
        Assert.True(contact.Normal.X > 0.99f);
    }

    [Fact]
    public void SpheresSeparatedDoNotOverlap()
    {
        var a = new SphereShape(1f);
        var b = new SphereShape(1f);

        Assert.False(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(2.5f, 0f, 0f), out _));
    }

    [Fact]
    public void TouchingSpheresCountAsOverlap()
    {
        var a = new SphereShape(0.5f);
        var b = new SphereShape(0.5f);

        Assert.True(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(1f, 0f, 0f), out ShapeContact contact));
        Assert.Equal(0f, contact.Depth, 4);
    }

    [Fact]
    public void BoxBoxUsesTheMinimumTranslationAxis()
    {
        var a = new BoxShape(Vector3.One);
        var b = new BoxShape(Vector3.One);

        Assert.True(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(0.5f, 0f, 0f), out ShapeContact contact));
        Assert.InRange(contact.Depth, 0.49f, 0.51f);
        Assert.True(MathF.Abs(contact.Normal.X) > 0.99f);
    }

    [Fact]
    public void BoxBoxSeparatedDoesNotOverlap()
    {
        var a = new BoxShape(Vector3.One);
        var b = new BoxShape(Vector3.One);

        Assert.False(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(2f, 0f, 0f), out _));
    }

    [Fact]
    public void SphereBoxDetectsAnOverlapAndTheReverseDirection()
    {
        var box = new BoxShape(new Vector3(2f));
        var sphere = new SphereShape(0.5f);
        Matrix4x4 sphereWorld = Matrix4x4.CreateTranslation(1.2f, 0f, 0f);

        Assert.True(CollisionQuery.TryGetContact(box, Identity, sphere, sphereWorld,
            out ShapeContact boxToSphere));
        Assert.True(CollisionQuery.TryGetContact(sphere, sphereWorld, box, Identity,
            out ShapeContact sphereToBox));
        Assert.True(boxToSphere.Normal.X > 0.99f);
        Assert.True(sphereToBox.Normal.X < -0.99f);
    }

    [Fact]
    public void SphereInsideBoxStillReportsAContact()
    {
        var box = new BoxShape(new Vector3(2f));
        var sphere = new SphereShape(0.25f);

        Assert.True(CollisionQuery.TryGetContact(box, Identity, sphere, Identity, out ShapeContact contact));
        Assert.True(contact.Depth > 0f);
    }

    [Fact]
    public void CapsulesOverlapAndSeparate()
    {
        var a = new CapsuleShape(0.5f, 2f);
        var b = new CapsuleShape(0.5f, 2f);

        Assert.True(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(0.8f, 0f, 0f), out _));
        Assert.False(CollisionQuery.TryGetContact(a, Identity, b,
            Matrix4x4.CreateTranslation(1.2f, 0f, 0f), out _));
    }

    [Fact]
    public void CapsuleBoxUsesTheCapsuleRadius()
    {
        var capsule = new CapsuleShape(0.5f, 2f);
        var box = new BoxShape(new Vector3(2f));

        Assert.True(CollisionQuery.TryGetContact(capsule, Matrix4x4.CreateTranslation(1.2f, 0f, 0f),
            box, Identity, out _));
        Assert.False(CollisionQuery.TryGetContact(capsule, Matrix4x4.CreateTranslation(1.7f, 0f, 0f),
            box, Identity, out _));
    }

    [Fact]
    public void CylinderIsTreatedAsACapsuleForNarrowPhase()
    {
        var cylinder = new CylinderShape(0.5f, 2f);
        var sphere = new SphereShape(0.5f);

        Assert.True(CollisionQuery.TryGetContact(cylinder, Identity, sphere,
            Matrix4x4.CreateTranslation(0.9f, 0f, 0f), out _));
        Assert.False(CollisionQuery.TryGetContact(cylinder, Identity, sphere,
            Matrix4x4.CreateTranslation(1.1f, 0f, 0f), out _));
    }

    [Fact]
    public void CollisionEnabledIsASubtreeSwitchAndPredicateFiltersObjects()
    {
        var scene = new SceneGraph();
        var a = new GameObject(name: "a");
        a.AddCollisionShape(new BoxShape(Vector3.One));
        var b = new GameObject(name: "b");
        b.AddCollisionShape(new BoxShape(Vector3.One));
        scene.Add(a);
        scene.Add(b);

        Assert.NotEmpty(CollisionQuery.Detect(scene));

        a.CollisionEnabled = false;
        Assert.Empty(CollisionQuery.Detect(scene));

        a.CollisionEnabled = true;
        Assert.Empty(CollisionQuery.Detect(scene, node => node != a));
    }

    [Fact]
    public void BoundsIntersectionAndExpansionAreConservative()
    {
        var a = new Bounds(new Vector3(-1f), new Vector3(1f));
        var b = new Bounds(new Vector3(1f, -0.5f, -0.5f), new Vector3(2f, 0.5f, 0.5f));

        Assert.True(a.Intersects(b));
        Assert.True(a.Expanded(0.1f).Intersects(b));
        Assert.False(new Bounds(new Vector3(3f), new Vector3(4f)).Intersects(a));
    }
}
