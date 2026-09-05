using RobotSimulation.Core.Geometry;
using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// GPU 网格（VBO/VAO/EBO）。顶点交错布局由 <see cref="VertexLayout"/> 定义，
/// 与 CPU 侧 MeshData 导出格式保持一致；本类不再自行维护布局数字。
/// </summary>
public class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo;
    private readonly int _indexCount;
    private bool _disposed = false;

    public Mesh(GL gl, float[] vertices, uint[] indices)
    {
        _gl = gl;
        _indexCount = indices.Length;

        // 创建 VAO / VBO / EBO
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

        // 设置顶点属性指针（stride 与偏移均取自 VertexLayout，单位字节）
        SetupAttribute(gl, 0, VertexLayout.PositionComponentCount, VertexLayout.PositionFloatOffset);
        SetupAttribute(gl, 1, VertexLayout.UvComponentCount, VertexLayout.UvFloatOffset);
        SetupAttribute(gl, 2, VertexLayout.NormalComponentCount, VertexLayout.NormalFloatOffset);
        SetupAttribute(gl, 3, VertexLayout.TangentComponentCount, VertexLayout.TangentFloatOffset);

        // 解绑
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

    /// <summary>绘制（三角形列表）。</summary>
    public unsafe void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_ebo != 0) _gl.DeleteBuffer(_ebo);
        _disposed = true;
    }
}
