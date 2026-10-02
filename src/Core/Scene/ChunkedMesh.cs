using System;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// A scene node owning one mesh chunk of an incrementally reconstructed surface: one
/// <see cref="MeshData"/> reused for the chunk's whole lifetime, so the renderer's data-reference-keyed
/// GPU cache sees a stable identity and a whole-chunk replacement becomes a partial re-upload instead
/// of an allocation. Built and driven by <see cref="MeshSink"/>; a host normally does not create one
/// directly.
/// </summary>
public sealed class ChunkedMesh : GameObject
{
    /// <summary>Stable identity of the chunk this node draws.</summary>
    public long ChunkId { get; }

    /// <summary>Chunk revision already applied (the protocol's out-of-order guard); <c>-1</c> before the first upsert.</summary>
    public long AppliedRevision { get; private set; } = -1;

    /// <summary>The chunk's geometry, reused in place across updates.</summary>
    public MeshData Mesh => MeshData!;

    /// <summary>
    /// Creates an empty chunk node at <paramref name="origin"/>.
    /// </summary>
    /// <param name="chunkId">Stable chunk identity.</param>
    /// <param name="origin">Chunk origin in world space (the node's position).</param>
    /// <param name="color">Base color; null uses a neutral light grey.</param>
    /// <param name="doubleSided">Whether the chunk's triangles render double-sided (default true: reconstructed surfaces may have arbitrary winding).</param>
    public ChunkedMesh(long chunkId, Vector3 origin, Vector4? color = null, bool doubleSided = true)
        : base(new MeshData(),
            new MaterialData
            {
                PassKind = RenderPassKind.Model,
                BaseColor = color ?? new Vector4(0.78f, 0.80f, 0.83f, 1f),
                DoubleSided = doubleSided,
            },
            name: $"mesh-chunk-{chunkId}")
    {
        ChunkId = chunkId;
        Transform.Position = origin;
    }

    /// <summary>
    /// Applies a whole-chunk replacement: moves the node to the update's origin and swaps the entire
    /// geometry in place. An update whose revision is not newer than <see cref="AppliedRevision"/> is
    /// ignored (out-of-order delivery).
    /// </summary>
    /// <param name="update">The update to apply; must be an upsert.</param>
    /// <returns>True when the geometry changed, false when the update was stale and dropped.</returns>
    /// <exception cref="ArgumentException"><paramref name="update"/> is a remove (handled by <see cref="MeshSink"/>).</exception>
    public bool Apply(in MeshChunkUpdate update)
    {
        if (update.Kind != MeshChunkKind.Upsert)
            throw new ArgumentException("ChunkedMesh.Apply expects an upsert; removes are handled by MeshSink.", nameof(update));

        if (update.Revision is { } revision && revision <= AppliedRevision)
            return false;

        Transform.Position = update.Origin;
        Mesh.SetGeometry(update.Vertices.Span, update.Indices.Span);
        if (update.Revision is { } applied)
            AppliedRevision = applied;
        return true;
    }
}
