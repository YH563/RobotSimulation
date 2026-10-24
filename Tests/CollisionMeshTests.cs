using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Tests;

/// <summary>
/// Mesh collision without a GL context: triangle/triangle, mesh/primitive, BVH revision invalidation,
/// and the incremental-mesh sink's optional collision shape.
/// </summary>
public class CollisionMeshTests
{
    private static MeshData Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        var mesh = new MeshData();
        uint ia = mesh.AddVertex(a, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint ib = mesh.AddVertex(b, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint ic = mesh.AddVertex(c, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        mesh.AddTriangle(ia, ib, ic);
        return mesh;
    }

    [Fact]
    public void CoplanarTrianglesOverlap()
    {
        var a = new MeshShape(Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY));
        var b = new MeshShape(Triangle(new Vector3(0.2f, 0.2f, 0f),
            new Vector3(1.2f, 0.2f, 0f), new Vector3(0.2f, 1.2f, 0f)));

        Assert.True(CollisionQuery.TryGetContact(a, Matrix4x4.Identity, b, Matrix4x4.Identity, out _));
    }

    [Fact]
    public void SeparatedTrianglesDoNotOverlap()
    {
        var a = new MeshShape(Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY));
        var b = new MeshShape(Triangle(new Vector3(0f, 0f, 1f),
            new Vector3(1f, 0f, 1f), new Vector3(0f, 1f, 1f)));

        Assert.False(CollisionQuery.TryGetContact(a, Matrix4x4.Identity, b, Matrix4x4.Identity, out _));
    }

    [Fact]
    public void MeshOverlapsSphereAndBox()
    {
        var mesh = new MeshShape(Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY));
        var sphere = new SphereShape(0.2f);
        var box = new BoxShape(new Vector3(0.5f));

        Assert.True(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, sphere,
            Matrix4x4.CreateTranslation(0.2f, 0.2f, 0f), out _));
        Assert.True(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, box,
            Matrix4x4.CreateTranslation(0.2f, 0.2f, 0f), out _));
        Assert.False(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, sphere,
            Matrix4x4.CreateTranslation(2f, 2f, 0f), out _));
    }

    [Fact]
    public void MeshBvhRebuildsWhenTheMeshRevisionChanges()
    {
        MeshData data = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var mesh = new MeshShape(data);
        var sphere = new SphereShape(0.2f);

        Assert.True(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, sphere,
            Matrix4x4.CreateTranslation(0.2f, 0.2f, 0f), out _));

        data.Clear();
        uint a = data.AddVertex(new Vector3(5f, 5f, 0f), Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint b = data.AddVertex(new Vector3(6f, 5f, 0f), Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint c = data.AddVertex(new Vector3(5f, 6f, 0f), Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        data.AddTriangle(a, b, c);

        Assert.False(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, sphere,
            Matrix4x4.CreateTranslation(0.2f, 0.2f, 0f), out _));
        Assert.True(CollisionQuery.TryGetContact(mesh, Matrix4x4.Identity, sphere,
            Matrix4x4.CreateTranslation(5.2f, 5.2f, 0f), out _));
    }

    [Fact]
    public void MeshSinkCanExposeReconstructedChunksToCollision()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene, enableCollision: true);
        sink.Push(Chunk(0f, 1));
        sink.Apply();

        var sphere = new SphereShape(0.2f);
        var probe = new GameObject(name: "probe");
        probe.AddCollisionShape(sphere);
        probe.Transform.Position = new Vector3(0.2f, 0.2f, 0f);
        scene.Add(probe);

        Assert.Contains(CollisionQuery.Detect(scene), c => c.A == probe || c.B == probe);

        sink.Push(Chunk(5f, 2));
        sink.Apply();

        Assert.DoesNotContain(CollisionQuery.Detect(scene), c => c.A == probe || c.B == probe);
    }

    [Fact]
    public void CollisionCanBeToggledAfterChunksExist()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);
        sink.Push(Chunk(0f, 1));
        sink.Apply();

        var probe = new GameObject(name: "probe");
        probe.AddCollisionShape(new SphereShape(0.2f));
        probe.Transform.Position = new Vector3(0.2f, 0.2f, 0f);
        scene.Add(probe);

        Assert.Empty(CollisionQuery.Detect(scene));

        sink.SetCollisionEnabled(true);
        Assert.NotEmpty(CollisionQuery.Detect(scene));

        sink.SetCollisionEnabled(false);
        Assert.Empty(CollisionQuery.Detect(scene));
    }

    private static MeshChunkUpdate Chunk(float z, long revision)
    {
        var vertices = new float[3 * VertexLayout.FloatsPerVertex];
        WriteVertex(vertices, 0, new Vector3(0f, 0f, z));
        WriteVertex(vertices, VertexLayout.FloatsPerVertex, new Vector3(1f, 0f, z));
        WriteVertex(vertices, 2 * VertexLayout.FloatsPerVertex, new Vector3(0f, 1f, z));
        return new MeshChunkUpdate(1, Vector3.Zero, vertices, new uint[] { 0, 1, 2 }, revision: revision);
    }

    private static void WriteVertex(float[] vertices, int offset, Vector3 position)
    {
        vertices[offset] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = position.Z;
        vertices[offset + 7] = 1f;
        vertices[offset + 8] = 1f;
    }
}
