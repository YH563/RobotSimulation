using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU mesh (VBO/VAO/EBO). The interleaved vertex layout is defined by <see cref="VertexLayout"/>,
/// matching the format exported by CPU-side MeshData; this class does not maintain its own layout numbers.
///
/// The mesh follows its data instead of copying it once: <see cref="Sync"/> compares the data's
/// <see cref="MeshData.Revision"/> with the revision it last uploaded and re-uploads only when it changed,
/// reallocating the buffers (DynamicDraw) only when the geometry outgrows them. This is what turns a
/// whole-chunk replacement into a partial re-upload at chunk granularity.
/// </summary>
public class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo;
    private int _indexCount;
    private int _vertexBytes;   // Bytes currently allocated on the VBO.
    private int _indexBytes;    // Bytes currently allocated on the EBO.
    private long _uploadedRevision = -1;
    private bool _disposed = false;

    /// <summary>Uploads an interleaved vertex array and its index buffer into a new VAO/VBO/EBO.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="vertices">Vertex data in the <see cref="VertexLayout"/> layout.</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public Mesh(GL gl, float[] vertices, uint[] indices)
    {
        _gl = gl;

        // Create VAO / VBO / EBO.
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        _ebo = gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        // Set up the vertex attribute pointers (stride and offset come from VertexLayout, in bytes).
        SetupAttribute(gl, 0, VertexLayout.PositionComponentCount, VertexLayout.PositionFloatOffset);
        SetupAttribute(gl, 1, VertexLayout.UvComponentCount, VertexLayout.UvFloatOffset);
        SetupAttribute(gl, 2, VertexLayout.NormalComponentCount, VertexLayout.NormalFloatOffset);
        SetupAttribute(gl, 3, VertexLayout.TangentComponentCount, VertexLayout.TangentFloatOffset);

        Upload(vertices, indices);

        // Unbind.
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Uploads a CPU <see cref="MeshData"/> and remembers the revision it reflects.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="data">The mesh to upload.</param>
    public Mesh(GL gl, MeshData data) : this(gl, data.ToInterleavedArray(), data.ToIndexArray())
    {
        _uploadedRevision = data.Revision;
    }

    /// <summary>Revision of the CPU mesh the GPU buffers currently reflect (diagnostics / tests).</summary>
    public long UploadedRevision => _uploadedRevision;

    /// <summary>
    /// Brings the GPU buffers up to date with <paramref name="data"/>, doing nothing when its
    /// <see cref="MeshData.Revision"/> is unchanged. An ordinary chunk replacement reuses the
    /// already-allocated buffers (<c>BufferSubData</c>); the buffers are reallocated only when the
    /// geometry grows past them.
    /// </summary>
    /// <param name="data">The mesh to sync.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The mesh has been disposed.</exception>
    public void Sync(MeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_disposed)
            throw new ObjectDisposedException(nameof(Mesh));

        if (_uploadedRevision == data.Revision)
            return;

        Upload(data.ToInterleavedArray(), data.ToIndexArray());
        _uploadedRevision = data.Revision;
    }

    private static unsafe void SetupAttribute(GL gl, uint location, int componentCount, int floatOffset)
    {
        gl.EnableVertexAttribArray(location);
        gl.VertexAttribPointer(location, componentCount, VertexAttribPointerType.Float, false,
            VertexLayout.BytesPerVertex, (void*)(floatOffset * sizeof(float)));
    }

    /// <summary>Uploads vertices and indices, growing the buffers only when needed.</summary>
    private unsafe void Upload(float[] vertices, uint[] indices)
    {
        // The EBO binding is part of the VAO's state, so bind the VAO around the uploads.
        _gl.BindVertexArray(_vao);

        int vertexBytes = vertices.Length * sizeof(float);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = vertices)
        {
            if (vertexBytes > _vertexBytes)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)vertexBytes, ptr, BufferUsageARB.DynamicDraw);
                _vertexBytes = vertexBytes;
            }
            else if (vertexBytes > 0)
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)vertexBytes, ptr);
            }
        }

        int indexBytes = indices.Length * sizeof(uint);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* ptr = indices)
        {
            if (indexBytes > _indexBytes)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)indexBytes, ptr, BufferUsageARB.DynamicDraw);
                _indexBytes = indexBytes;
            }
            else if (indexBytes > 0)
            {
                _gl.BufferSubData(BufferTargetARB.ElementArrayBuffer, 0, (nuint)indexBytes, ptr);
            }
        }

        _indexCount = indices.Length;

        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>Draws (as a triangle list).</summary>
    public unsafe void Draw()
    {
        if (_indexCount == 0)
            return;

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    /// <summary>Deletes the VAO/VBO/EBO; safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
        _disposed = true;
    }
}
