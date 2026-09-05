using System;
using System.Numerics;
using RobotSimulation.Core.Rendering;
using Silk.NET.OpenGL;

namespace RobotSimulation.OpenGL.Device;

/// <summary>
/// 渲染上下文（设备层）：持有 GL 实例，实现 <see cref="IRenderContext"/> 对外服务。
/// GL 类型只允许出现在本类型内部——对外（Core/上层）只暴露
/// <see cref="IRenderContext"/> 纯接口与纯数据参数（Vector4 颜色）。
/// </summary>
public sealed class GraphicsContext : IRenderContext
{
    /// <summary>同程序集（渲染实现层）经此取得 GL；对 Core/上层不暴露。</summary>
    internal GL NativeGl => _gl;

    /// <inheritdoc />
    public event Action<int, int>? Resized;

    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// 设备入口：宿主把渲染线程上创建好的 GL 实例注入，随即被适配为
    /// <see cref="IRenderContext"/>，之后 GL 不再向外传播。
    /// </summary>
    public GraphicsContext(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));

        // 默认开启深度测试和背面剔除，确保不透明几何体正确渲染
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
    }

    /// <inheritdoc />
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        Resized?.Invoke(width, height);
    }

    /// <inheritdoc />
    public void Clear(Vector4 clearColor, bool clearDepth = true)
    {
        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        _gl.Clear(clearDepth
            ? ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit
            : ClearBufferMask.ColorBufferBit);
    }

    /// <summary>着色器程序统一由渲染器（Renderer）从内嵌标准目录创建并释放，本类型不再持有。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
