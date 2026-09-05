using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU 点缓冲（GL_POINTS），对应 Core 的 <see cref="PointCloudData"/>。
/// 位置在 attribute 0；点大小/颜色由 Point 通道着色器的 uniform 给定。
/// </summary>
public sealed class PointMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo;
    private readonly uint _vertexCount;
    private bool _disposed;

    public PointMesh(GL gl, PointCloudData data)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _vertexCount = (uint)data.Count;
        float[] positions = data.ToPositionArray();

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

        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Points, 0, _vertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        _disposed = true;
    }
}
