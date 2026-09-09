using System;
using System.Numerics;
using RobotSimulation.Core.Rendering;
using Silk.NET.OpenGL;

namespace RobotSimulation.OpenGL.Device;

/// <summary>
/// Rendering context (device layer): owns the GL instance and implements <see cref="IRenderContext"/>.
/// GL types are allowed only inside this type — externally (Core/upper layers) only the
/// <see cref="IRenderContext"/> pure interface and pure-data parameters (Vector4 colors) are exposed.
/// </summary>
public sealed class GraphicsContext : IRenderContext
{
    /// <summary>Used by the same assembly (the rendering implementation layer) to access GL; not exposed to Core/upper layers.</summary>
    internal GL NativeGl => _gl;

    /// <inheritdoc />
    public event Action<int, int>? Resized;

    private readonly GL _gl;
    private bool _disposed;

    /// <summary>
    /// Device entry point: the host injects a GL instance created on the render thread, which is adapted
    /// into an <see cref="IRenderContext"/>; after that GL no longer propagates outward.
    /// </summary>
    public GraphicsContext(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));

        // Enable depth testing and back-face culling by default to render opaque geometry correctly.
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

    /// <summary>Shader programs are created and released by the renderer (Renderer) from the embedded standard catalog; this type holds none.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
