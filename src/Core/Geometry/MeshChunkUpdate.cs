using System;
using System.Numerics;

namespace RobotSimulation.Core.Geometry;

/// <summary>What a <see cref="MeshChunkUpdate"/> does to its chunk.</summary>
public enum MeshChunkKind
{
    /// <summary>Create or replace the chunk's whole mesh.</summary>
    Upsert = 0,

    /// <summary>Drop the chunk (it left the producer's map).</summary>
    Remove = 1,
}

/// <summary>
/// One incrementally updated mesh chunk, the unit of the external write protocol (see the
/// "incremental meshes (Sink)" section of the Core docs). A reconstruction backend owns the
/// reconstruction and emits this message; the library turns it into a scene node and a partial GPU
/// upload. A chunk is a fixed, axis-aligned region of world space identified by a stable
/// <see cref="ChunkId"/>; an <see cref="MeshChunkKind.Upsert"/> carries the chunk's complete geometry
/// (a whole-chunk replacement, never a per-triangle delta), and <see cref="Remove"/> carries none.
///
/// The message is transport-agnostic pure data: an in-process object, a shared-memory view or a
/// decoded network message all produce it. Vertices use the <see cref="VertexLayout"/> interleaved
/// layout and are chunk-local; <see cref="Origin"/> is the chunk's world position, so large worlds
/// keep their float precision.
/// </summary>
public readonly struct MeshChunkUpdate
{
    /// <summary>Stable identity of the chunk; the same id always means the same region/node.</summary>
    public long ChunkId { get; }

    /// <summary>Whether this update replaces the chunk's mesh or removes the chunk.</summary>
    public MeshChunkKind Kind { get; }

    /// <summary>World-space origin of the chunk (its node's position); vertices are relative to it.</summary>
    public Vector3 Origin { get; }

    /// <summary>
    /// Vertices in the <see cref="VertexLayout"/> interleaved layout (pos3 | uv2 | normal3 | tangent3),
    /// chunk-local. Empty for <see cref="MeshChunkKind.Remove"/>.
    /// </summary>
    public ReadOnlyMemory<float> Vertices { get; }

    /// <summary>Triangle-list indices into <see cref="Vertices"/> (three per triangle). Empty for <see cref="MeshChunkKind.Remove"/>.</summary>
    public ReadOnlyMemory<uint> Indices { get; }

    /// <summary>
    /// Optional chunk-level revision for out-of-order handling: when set, the sink drops an update whose
    /// revision is older than the one already applied to the chunk. Null means "always apply".
    /// </summary>
    public long? Revision { get; }

    /// <summary>Optional world-space bounds, advisory (culling/picking); computed from the vertices when absent.</summary>
    public Bounds? Bounds { get; }

    /// <summary>Whether <see cref="Bounds"/> was supplied.</summary>
    public bool HasBounds => Bounds.HasValue;

    /// <summary>
    /// Builds a whole-chunk replacement.
    /// </summary>
    /// <param name="chunkId">Stable chunk identity.</param>
    /// <param name="origin">Chunk origin in world space.</param>
    /// <param name="vertices">Interleaved vertices (<see cref="VertexLayout.FloatsPerVertex"/> floats each).</param>
    /// <param name="indices">Triangle-list indices.</param>
    /// <param name="revision">Optional chunk revision (null = always apply).</param>
    /// <param name="bounds">Optional world-space bounds.</param>
    /// <exception cref="ArgumentException">The vertex array is not a whole number of vertices, or the index count is not a multiple of 3.</exception>
    public MeshChunkUpdate(long chunkId, Vector3 origin, ReadOnlyMemory<float> vertices,
        ReadOnlyMemory<uint> indices, long? revision = null, Bounds? bounds = null)
    {
        if (vertices.Length % VertexLayout.FloatsPerVertex != 0)
            throw new ArgumentException(
                $"Vertex array length must be a multiple of {VertexLayout.FloatsPerVertex} (FloatsPerVertex).",
                nameof(vertices));
        if (indices.Length % 3 != 0)
            throw new ArgumentException("Index count must be a multiple of 3 (a triangle list).", nameof(indices));

        ChunkId = chunkId;
        Kind = MeshChunkKind.Upsert;
        Origin = origin;
        Vertices = vertices;
        Indices = indices;
        Revision = revision;
        Bounds = bounds;
    }

    private MeshChunkUpdate(long chunkId)
    {
        ChunkId = chunkId;
        Kind = MeshChunkKind.Remove;
        Origin = default;
        Vertices = default;
        Indices = default;
        Revision = null;
        Bounds = null;
    }

    /// <summary>Builds a remove message that drops the chunk.</summary>
    /// <param name="chunkId">Stable chunk identity.</param>
    public static MeshChunkUpdate Remove(long chunkId) => new(chunkId);
}

/// <summary>
/// A batch of <see cref="MeshChunkUpdate"/> handed over as one unit. Within a batch a later update of the
/// same chunk wins; the whole batch is applied at a frame boundary, in order (see <c>MeshSink.Apply</c>).
/// </summary>
public readonly struct MeshChunkBatch
{
    /// <summary>The updates in the batch, applied in order.</summary>
    public ReadOnlyMemory<MeshChunkUpdate> Updates { get; }

    /// <summary>Wraps a set of updates as one batch.</summary>
    /// <param name="updates">Updates, in application order.</param>
    public MeshChunkBatch(ReadOnlyMemory<MeshChunkUpdate> updates)
    {
        Updates = updates;
    }
}
