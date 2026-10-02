using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// In-process receiving end of the external write protocol for incrementally reconstructed meshes.
/// A reconstruction backend calls <see cref="Push(in MeshChunkUpdate)"/> (from any thread) with whole-chunk
/// replacements; the scene owner thread drains them with <see cref="Apply"/> at its frame boundary, which
/// creates/replaces/removes one <see cref="ChunkedMesh"/> per chunk id and leaves the renderer to upload
/// only the chunks whose revision changed.
///
/// This is the mesh counterpart of feeding a stream into <c>PointCloud</c>, with one deliberate difference:
/// a chunk keeps a single <see cref="MeshData"/> for its whole lifetime (see <see cref="ChunkedMesh"/>),
/// because the renderer caches GPU buffers by data reference — replacing the object every update would
/// allocate and release on every frame.
/// </summary>
public sealed class MeshSink
{
    private readonly SceneGraph _scene;
    private readonly Vector4? _color;
    private readonly bool _doubleSided;

    // Producers only touch the queue; the dictionary is owner-thread-only (see Apply).
    private readonly ConcurrentQueue<MeshChunkUpdate> _pending = new();
    private readonly Dictionary<long, ChunkedMesh> _chunks = new();

    /// <summary>
    /// Creates a sink targeting <paramref name="scene"/>. <see cref="Apply"/> must run on that scene's owner
    /// thread, which is where its frame boundary runs.
    /// </summary>
    /// <param name="scene">Scene the chunk nodes are added to.</param>
    /// <param name="color">Base color of created chunk nodes; null uses <see cref="ChunkedMesh"/>'s default.</param>
    /// <param name="doubleSided">Whether created chunk nodes render double-sided (default true).</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public MeshSink(SceneGraph scene, Vector4? color = null, bool doubleSided = true)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _color = color;
        _doubleSided = doubleSided;
    }

    /// <summary>Number of updates pushed but not yet applied (diagnostics).</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Number of chunks currently held by the sink.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>
    /// Queues one update (any thread). The buffers backing an upsert must not be mutated after the call.
    /// </summary>
    public void Push(in MeshChunkUpdate update) => _pending.Enqueue(update);

    /// <summary>Queues every update of a batch, in order (any thread).</summary>
    /// <param name="batch">The batch to enqueue.</param>
    public void Push(in MeshChunkBatch batch)
    {
        ReadOnlySpan<MeshChunkUpdate> updates = batch.Updates.Span;
        for (int i = 0; i < updates.Length; i++)
            _pending.Enqueue(updates[i]);
    }

    /// <summary>
    /// Drains the queue on the scene owner thread: upserts create or replace the chunk's node (its geometry
    /// is swapped in place), removes detach it. Call it once per frame, before rendering, alongside
    /// <see cref="SceneGraph.ApplyPendingChanges"/>.
    /// </summary>
    /// <returns>Number of updates actually applied (stale revisions and removes of unknown chunks are not counted).</returns>
    /// <exception cref="InvalidOperationException">Called from a thread other than the scene's owner.</exception>
    public int Apply()
    {
        if (!_scene.IsOwnerThread)
            throw new InvalidOperationException(
                "MeshSink.Apply must run on the scene owner thread (the frame-loop thread); producers use Push from any thread.");

        int applied = 0;
        while (_pending.TryDequeue(out MeshChunkUpdate update))
        {
            if (update.Kind == MeshChunkKind.Remove)
            {
                if (_chunks.Remove(update.ChunkId, out ChunkedMesh? removed))
                {
                    _scene.Remove(removed);
                    applied++;
                }
                continue;
            }

            if (!_chunks.TryGetValue(update.ChunkId, out ChunkedMesh? node))
            {
                node = new ChunkedMesh(update.ChunkId, update.Origin, _color, _doubleSided);
                _chunks.Add(update.ChunkId, node);
                _scene.Add(node);
                node.Apply(update);
                applied++;
            }
            else if (node.Apply(update))
            {
                applied++;
            }
        }

        return applied;
    }

    /// <summary>Gets the node currently held for a chunk id (owner thread), for diagnostics/tests.</summary>
    /// <param name="chunkId">Chunk identity.</param>
    /// <param name="node">The chunk node when present.</param>
    public bool TryGetChunk(long chunkId, out ChunkedMesh? node) => _chunks.TryGetValue(chunkId, out node);

    /// <summary>Removes every chunk from the scene and forgets it (owner thread). Queued updates are left alone.</summary>
    public void Clear()
    {
        if (!_scene.IsOwnerThread)
            throw new InvalidOperationException("MeshSink.Clear must run on the scene owner thread.");

        foreach (ChunkedMesh node in _chunks.Values)
            _scene.Remove(node);
        _chunks.Clear();
    }
}
