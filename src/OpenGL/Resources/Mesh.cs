using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU mesh (VBO/VAO/EBO). The interleaved vertex layout is defined by <see cref="VertexLayout"/>,
/// matching the format exported by CPU-side MeshData; this class does not maintain its own layout numbers.
/// </summary>
public class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo;
    private readonly int _indexCount;
    private bool _disposed = false;

    /// <summary>Uploads an interleaved vertex array and its index buffer into a new VAO/VBO/EBO.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="vertices">Vertex data in the <see cref="VertexLayout"/> layout.</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public Mesh(GL gl, float[] vertices, uint[] indices)
    {
        _gl = gl;
        _indexCount = indices.Length;

        // Create VAO / VBO / EBO.
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        _ebo = gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Set up the vertex attribute pointers (stride and offset come from VertexLayout, in bytes).
        SetupAttribute(gl, 0, VertexLayout.PositionComponentCount, VertexLayout.PositionFloatOffset);
        SetupAttribute(gl, 1, VertexLayout.UvComponentCount, VertexLayout.UvFloatOffset);
        SetupAttribute(gl, 2, VertexLayout.NormalComponentCount, VertexLayout.NormalFloatOffset);
        SetupAttribute(gl, 3, VertexLayout.TangentComponentCount, VertexLayout.TangentFloatOffset);

        // Unbind.
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    private static unsafe void SetupAttribute(GL gl, uint location, int componentCount, int floatOffset)
    {
        gl.EnableVertexAttribArray(location);
        gl.VertexAttribPointer(location, componentCount, VertexAttribPointerType.Float, false,
            VertexLayout.BytesPerVertex, (void*)(floatOffset * sizeof(float)));
    }

    /// <summary>Draws (as a triangle list).</summary>
    public unsafe void Draw()
    {
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
