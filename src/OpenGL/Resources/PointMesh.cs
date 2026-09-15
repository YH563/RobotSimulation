using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU point buffer (GL_POINTS) backing a Core <see cref="PointCloud2Data"/>.
/// Position is uploaded to attribute 0; if the cloud carries per-point colors
/// (<see cref="PointCloud2Data.HasColor"/>) they are uploaded to attribute 1,
/// otherwise the Point pass shader uses a uniform color. Point size is a uniform.
///
/// The mesh follows the cloud instead of copying it once: <see cref="Sync"/> uploads only the slots that
/// changed since the previous call (<see cref="PointCloud2Data.GetSlotRuns"/>) and reallocates the store
/// only when the cloud's capacity grows. A cloud can also be a ring
/// (<see cref="PointCloud2Data.FirstSlot"/>), in which case <see cref="Draw"/> issues up to two draw
/// calls rather than moving the points.
/// </summary>
public sealed class PointMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _colorVbo;
    private readonly bool _hasColor;

    // Reused staging buffer: managed data has to be copied (and converted to float) before it goes to GL.
    private float[] _scratch = Array.Empty<float>();

    private uint _allocatedCapacity;   // Slots the vertex buffers were allocated for.
    private int _capacity;             // Ring capacity of the cloud at the last sync.
    private int _vertexCount;          // Points to draw (the cloud's logical count).
    private int _firstSlot;            // Physical slot of logical point 0.
    private long _uploadedRevision = -1;
    private bool _disposed;

    /// <summary>Creates the buffers for a cloud and uploads its current content.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="data">The CPU-side cloud being made drawable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> or <paramref name="data"/> is null.</exception>
    public PointMesh(GL gl, PointCloud2Data data)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(data);

        _gl = gl;
        _hasColor = data.HasColor;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.EnableVertexAttribArray(0);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        }

        if (_hasColor)
        {
            _colorVbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _colorVbo);
            gl.EnableVertexAttribArray(1);
            unsafe
            {
                gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            }
        }
        else
        {
            _colorVbo = 0;
        }

        gl.BindVertexArray(0);

        Sync(data);
    }

    /// <summary>Slots the vertex buffers are currently allocated for (diagnostics / tests).</summary>
    public uint AllocatedCapacity => _allocatedCapacity;

    /// <summary>Data revision the GPU buffers reflect (diagnostics / tests).</summary>
    public long UploadedRevision => _uploadedRevision;

    /// <summary>
    /// Brings the GPU buffers up to date with the cloud, doing nothing when nothing changed since the
    /// previous call. Otherwise it uploads the cloud's accumulated change window — or everything, when
    /// the store had to grow or this mesh fell behind that window (it is not the one the window was
    /// accumulated for).
    /// </summary>
    /// <param name="data">The cloud this mesh draws; it must be the cloud the mesh was created for, because the channel layout is fixed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
    public void Sync(PointCloud2Data data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfDisposed();

        if (_uploadedRevision == data.Revision && _capacity == data.Capacity)
            return;

        bool storeChanged = _allocatedCapacity != (uint)data.Capacity;
        if (storeChanged)
            AllocateStore(data.Capacity);

        PointCloud2Data.DirtyWindow dirty = data.Dirty;
        bool incremental = !storeChanged && _uploadedRevision >= 0 && _uploadedRevision == dirty.FromRevision;

        if (incremental)
            UploadRange(data, dirty.Start, dirty.Count);
        else
            UploadRange(data, 0, data.Count);

        _capacity = data.Capacity;
        _vertexCount = data.Count;
        _firstSlot = data.FirstSlot;
        _uploadedRevision = data.Revision;
        data.ResetDirty();
    }

    /// <summary>Draws every point (as GL_POINTS), in two ranges when the cloud's ring wraps.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vao);

        int first = _firstSlot;
        int remaining = _vertexCount;
        while (remaining > 0 && _capacity > 0)
        {
            int length = Math.Min(remaining, _capacity - first);
            _gl.DrawArrays(PrimitiveType.Points, first, (uint)length);
            remaining -= length;
            first = 0;
        }

        _gl.BindVertexArray(0);
    }

    /// <summary>Deletes the VAO/VBO and the optional color buffer; safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_colorVbo != 0) _gl.DeleteBuffer(_colorVbo);
        _disposed = true;
    }

    /// <summary>Allocates the store for a new capacity (orphaning whatever the old one held).</summary>
    private void AllocateStore(int capacity)
    {
        AllocateEmpty(_vbo, capacity * 3 * sizeof(float));
        if (_hasColor)
            AllocateEmpty(_colorVbo, capacity * 4 * sizeof(float));
        _allocatedCapacity = (uint)capacity;
    }

    private void AllocateEmpty(uint buffer, int bytes)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        unsafe
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)bytes, null, BufferUsageARB.DynamicDraw);
        }
    }

    /// <summary>Uploads logical points [<paramref name="start"/>, start + count), in at most two runs.</summary>
    private void UploadRange(PointCloud2Data data, int start, int count)
    {
        if (count <= 0)
            return;

        Span<PointCloud2Data.SlotRun> runs = stackalloc PointCloud2Data.SlotRun[2];
        int runCount = data.GetSlotRuns(start, count, runs);
        for (int i = 0; i < runCount; i++)
        {
            PointCloud2Data.SlotRun run = runs[i];
            UploadPositions(data, run.Slot, run.Count);
            if (_hasColor)
                UploadColors(data, run.Slot, run.Count);
        }
    }

    private void UploadPositions(PointCloud2Data data, int slot, int count)
    {
        Span<float> staging = PrepareStaging(count * 3);
        data.CopySlotPositions(slot, count, staging);
        Upload(_vbo, slot, staging, 3);
    }

    private void UploadColors(PointCloud2Data data, int slot, int count)
    {
        Span<float> staging = PrepareStaging(count * 4);
        if (!data.CopySlotColors(slot, count, staging))
            return;
        Upload(_colorVbo, slot, staging, 4);
    }

    /// <summary>Writes a staging buffer into <paramref name="buffer"/> at the byte offset of <paramref name="slot"/>.</summary>
    private void Upload(uint buffer, int slot, ReadOnlySpan<float> staging, int componentsPerPoint)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        unsafe
        {
            fixed (float* ptr = staging)
            {
                _gl.BufferSubData(
                    BufferTargetARB.ArrayBuffer,
                    (nint)(slot * componentsPerPoint * sizeof(float)),
                    (nuint)(staging.Length * sizeof(float)),
                    ptr);
            }
        }
    }

    private Span<float> PrepareStaging(int floats)
    {
        if (_scratch.Length < floats)
            _scratch = new float[floats];
        return _scratch.AsSpan(0, floats);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PointMesh));
    }
}
