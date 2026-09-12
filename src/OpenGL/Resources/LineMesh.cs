using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU line buffer (GL_LINES: every two vertices one segment), corresponding to Core's <see cref="LineData"/>.
/// Position is at attribute 0; if the data carries per-vertex colors they are also uploaded to
/// attribute 1, otherwise the Line pass shader uses the uColor uniform for a uniform color.
/// </summary>
public sealed class LineMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _colorVbo;
    private readonly uint _vertexCount;
    private bool _disposed;

    /// <summary>Uploads the line set's positions (and its per-vertex colors, when present) into fresh GPU buffers.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="data">The CPU-side line set being made drawable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> is null.</exception>
    public LineMesh(GL gl, LineData data)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _vertexCount = (uint)data.VertexCount;
        float[] positions = data.ToPositionArray();
        float[]? colors = data.ToColorArray();

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = positions)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }
        gl.EnableVertexAttribArray(0);
        unsafe
        {
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        }

        _colorVbo = colors is null ? 0 : SetupColors(gl, colors);

        gl.BindVertexArray(0);
    }

    private uint SetupColors(GL gl, float[] colors)
    {
        uint buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
        unsafe
        {
            fixed (float* ptr = colors)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(colors.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }
        gl.EnableVertexAttribArray(1);
        unsafe
        {
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        }
        return buffer;
    }

    /// <summary>Draws every segment (as GL_LINES) and unbinds the VAO again.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
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
}
