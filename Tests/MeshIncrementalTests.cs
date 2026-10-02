using System.Numerics;
using System.Threading;

using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Tests;

/// <summary>
/// The incremental-mesh path, tested without a GL context: the data layer's whole-geometry replacement,
/// the protocol message, and the sink's frame-boundary application (identity reuse, out-of-order drops,
/// removes, and the owner-thread contract). GPU upload policy is the renderer's, not the data layer's.
/// </summary>
public class MeshIncrementalTests
{
    /// <summary>Interleaved vertices (pos3 | uv2 | normal3 | tangent3) for one triangle, unit normal/tangent.</summary>
    private static float[] Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        var v = new float[3 * VertexLayout.FloatsPerVertex];
        WriteVertex(v, 0, a);
        WriteVertex(v, VertexLayout.FloatsPerVertex, b);
        WriteVertex(v, 2 * VertexLayout.FloatsPerVertex, c);
        return v;
    }

    private static void WriteVertex(float[] v, int offset, Vector3 p)
    {
        v[offset] = p.X;
        v[offset + 1] = p.Y;
        v[offset + 2] = p.Z;
        // uv2 at +3, normal3 at +5, tangent3 at +8.
        v[offset + 7] = 1f;   // normal = +Z
        v[offset + 8] = 1f;   // tangent = +X
    }

    private static readonly uint[] OneTriangle = { 0, 1, 2 };

    private static MeshChunkUpdate Upsert(long id, Vector3 origin, float[] vertices, long? revision = null)
        => new(id, origin, vertices, OneTriangle, revision);

    // ---- MeshData -----------------------------------------------------------------

    [Fact]
    public void SetGeometryDecodesVertexLayoutAndBumpsRevision()
    {
        var data = new MeshData();
        long before = data.Revision;

        data.SetGeometry(Triangle(new(1, 2, 3), new(4, 5, 6), new(7, 8, 9)), OneTriangle);

        Assert.Equal(3, data.VertexCount);
        Assert.Equal(1, data.TriangleCount);
        Assert.Equal(new Vector3(1, 2, 3), data.Positions[0]);
        Assert.Equal(new Vector3(0, 0, 1), data.Normals[0]);
        Assert.Equal(new Vector3(1, 0, 0), data.Tangents[2]);
        Assert.True(data.Revision > before);
    }

    [Fact]
    public void SetGeometryRoundTripsThroughInterleavedArray()
    {
        var data = new MeshData();
        float[] source = Triangle(new(1, 2, 3), new(4, 5, 6), new(7, 8, 9));

        data.SetGeometry(source, OneTriangle);

        Assert.Equal(source, data.ToInterleavedArray());
        Assert.Equal(OneTriangle, data.ToIndexArray());
    }

    [Fact]
    public void SetGeometryRejectsMalformedBuffers()
    {
        var data = new MeshData();

        Assert.Throws<ArgumentException>(() => data.SetGeometry(new float[3], OneTriangle));
        Assert.Throws<ArgumentException>(() => data.SetGeometry(Triangle(default, default, default), new uint[] { 0, 1 }));
        Assert.Throws<ArgumentException>(() => data.SetGeometry(Triangle(default, default, default), new uint[] { 0, 1, 9 }));
    }

    [Fact]
    public void ClearDropsGeometryAndBumpsRevisionOnce()
    {
        var data = new MeshData();
        data.SetGeometry(Triangle(default, default, default), OneTriangle);
        long filled = data.Revision;

        data.Clear();

        Assert.True(data.IsEmpty);
        Assert.Equal(0, data.VertexCount);
        Assert.Equal(filled + 1, data.Revision);

        data.Clear();   // already empty: no further revision bump
        Assert.Equal(filled + 1, data.Revision);
    }

    // ---- MeshChunkUpdate ----------------------------------------------------------

    [Fact]
    public void RemoveCarriesNoGeometry()
    {
        MeshChunkUpdate remove = MeshChunkUpdate.Remove(42);

        Assert.Equal(42, remove.ChunkId);
        Assert.Equal(MeshChunkKind.Remove, remove.Kind);
        Assert.True(remove.Vertices.IsEmpty);
        Assert.True(remove.Indices.IsEmpty);
        Assert.Null(remove.Revision);
    }

    // ---- MeshSink -----------------------------------------------------------------

    [Fact]
    public void UpsertCreatesAChunkNodeAndReusesItOnTheNextUpdate()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);

        sink.Push(Upsert(7, new Vector3(1, 0, 0), Triangle(default, default, default)));
        Assert.Equal(1, sink.PendingCount);
        Assert.Equal(1, sink.Apply());

        Assert.True(sink.TryGetChunk(7, out ChunkedMesh? node));
        Assert.NotNull(node);
        Assert.Equal(3, node!.Mesh.VertexCount);
        Assert.Equal(new Vector3(1, 0, 0), node.Transform.Position);
        Assert.Contains(node, scene.Roots);

        // A second update must reuse the very same node/MeshData (stable identity -> stable GPU buffers).
        MeshData mesh = node.Mesh;
        sink.Push(Upsert(7, new Vector3(2, 0, 0), Triangle(default, default, default)));
        Assert.Equal(1, sink.Apply());

        Assert.True(sink.TryGetChunk(7, out ChunkedMesh? again));
        Assert.Same(node, again);
        Assert.Same(mesh, again!.Mesh);
        Assert.Equal(new Vector3(2, 0, 0), again.Transform.Position);
    }

    [Fact]
    public void StaleRevisionsAreDropped()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);

        sink.Push(Upsert(1, default, Triangle(default, default, default), revision: 5));
        Assert.Equal(1, sink.Apply());

        sink.Push(Upsert(1, default, Triangle(default, default, default), revision: 4));
        Assert.Equal(0, sink.Apply());

        sink.Push(Upsert(1, default, Triangle(default, default, default), revision: 5));   // equal is stale too
        Assert.Equal(0, sink.Apply());

        sink.Push(Upsert(1, default, Triangle(default, default, default), revision: 6));
        Assert.Equal(1, sink.Apply());
    }

    [Fact]
    public void RemoveDetachesTheChunkAndUnknownRemovesAreNoOps()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);

        sink.Push(Upsert(7, default, Triangle(default, default, default)));
        Assert.Equal(1, sink.Apply());
        Assert.True(sink.TryGetChunk(7, out ChunkedMesh? node));

        sink.Push(MeshChunkUpdate.Remove(7));
        Assert.Equal(1, sink.Apply());
        Assert.False(sink.TryGetChunk(7, out _));
        Assert.DoesNotContain(node!, scene.Roots);

        sink.Push(MeshChunkUpdate.Remove(7));
        Assert.Equal(0, sink.Apply());
    }

    [Fact]
    public void BatchIsAppliedInOrderAndLaterUpdateOfAChunkWins()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);

        var batch = new MeshChunkBatch(new[]
        {
            Upsert(1, new Vector3(1, 0, 0), Triangle(default, default, default)),
            Upsert(2, new Vector3(2, 0, 0), Triangle(default, default, default)),
            Upsert(1, new Vector3(3, 0, 0), Triangle(default, default, default)),
        });

        sink.Push(batch);
        Assert.Equal(3, sink.PendingCount);
        Assert.Equal(3, sink.Apply());

        Assert.True(sink.TryGetChunk(1, out ChunkedMesh? one));
        Assert.Equal(new Vector3(3, 0, 0), one!.Transform.Position);
        Assert.True(sink.TryGetChunk(2, out _));
    }

    [Fact]
    public void PushIsSafeFromAnotherThreadButApplyMustRunOnTheOwner()
    {
        var scene = new SceneGraph();
        var sink = new MeshSink(scene);

        var thread = new Thread(() => sink.Push(Upsert(9, default, Triangle(default, default, default))))
        {
            IsBackground = true,
        };
        thread.Start();
        thread.Join();

        Assert.Equal(1, sink.PendingCount);

        Exception? failure = null;
        var applyThread = new Thread(() => failure = Record.Exception(() => sink.Apply())) { IsBackground = true };
        applyThread.Start();
        applyThread.Join();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(1, sink.PendingCount);   // the wrong call applied nothing
        Assert.Equal(1, sink.Apply());        // the owner still can
    }
}
