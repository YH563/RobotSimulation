using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU point buffer (GL_POINTS) backing a Core <see cref="PointCloud2Data"/>.
/// Position is uploaded to attribute 0; if the cloud carries per-point colors
/// (<see cref="PointCloud2Data.HasColor"/>) they are uploaded to attribute 1,
/// otherwise the Point pass shader uses a uniform color. Point size is a uniform.
/// </summary>
public sealed class PointMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _colorVbo;
    private readonly uint _vertexCount;
    private bool _disposed;

    /// <summary>Uploads the cloud's positions (and its per-point colors, when present) into fresh GPU buffers.</summary>
    /// <param name="gl">The GL facade to create the buffers on.</param>
    /// <param name="data">The CPU-side cloud being made drawable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="gl"/> is null.</exception>
    public PointMesh(GL gl, PointCloud2Data data)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _vertexCount = (uint)data.Count;
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

    /// <summary>Draws every point (as GL_POINTS) and unbinds the VAO again.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Points, 0, _vertexCount);
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
