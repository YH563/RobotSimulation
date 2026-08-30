using Silk.NET.OpenGL;
using System;
using System.Drawing;

namespace RobotSimulation.Core.Rendering;

public class GraphicsContext : IDisposable
{
    /// <summary>
    /// 着色器缓存
    /// </summary>
    public ShaderCache ShaderCache { get; }
    
    /// <summary>
    /// 当视口大小发生变化时触发，参数为新的宽度和高度。
    /// </summary>
    public event Action<int, int> Resized;
    
    private readonly GL _gl;
    private bool _disposed = false;

    public GraphicsContext(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        ShaderCache = new ShaderCache(_gl);

        // 默认开启深度测试和背面剔除
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
    }
    
    /// <summary>
    /// 更新视口并触发 Resized 事件。
    /// </summary>
    /// <param name="width">新视口宽度</param>
    /// <param name="height">新视口高度</param>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        Resized?.Invoke(width, height);
    }
    
    /// <summary>
    /// 设置清屏颜色。
    /// </summary>
    public void ClearColor(Color color) => _gl.ClearColor(color);

    /// <summary>
    /// 清除指定缓冲区（通常为颜色和深度）。
    /// </summary>
    public void Clear(ClearBufferMask mask) => _gl.Clear(mask);
    

    /// <summary>
    /// 释放着色器缓存及其他资源（不释放 _gl，因为它由外部管理）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        ShaderCache.Dispose();
        _disposed = true;
    }
}