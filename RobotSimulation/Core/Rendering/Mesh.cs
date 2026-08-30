using Silk.NET.OpenGL;
using System;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 网格类
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
        
        // 创建 vao
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        
        // 创建 vbo
        _vbo =  gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }
        
        // 创建 ebo
        _ebo = gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        unsafe
        {
            // 设置顶点属性指针
            const uint posLoc = 0;  // 位置
            _gl.EnableVertexAttribArray(posLoc);
            _gl.VertexAttribPointer(posLoc, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), (void*)0);

            const uint uvLoc = 1;  // uv
            _gl.EnableVertexAttribArray(uvLoc);
            _gl.VertexAttribPointer(uvLoc, 2, VertexAttribPointerType.Float, false, 11 * sizeof(float), (void*)(3 * sizeof(float)));

            const uint normalLoc = 2;  // 法向量
            _gl.EnableVertexAttribArray(normalLoc);
            _gl.VertexAttribPointer(normalLoc, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), (void*)(5 * sizeof(float)));

            const uint tangentLoc = 3;  // 切向量
            _gl.EnableVertexAttribArray(tangentLoc);
            _gl.VertexAttribPointer(tangentLoc, 3, VertexAttribPointerType.Float, false, 11 * sizeof(float), (void*)(8 * sizeof(float)));
        }
        // 解绑
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
    }

    /// <summary>
    /// 绘制
    /// </summary>
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