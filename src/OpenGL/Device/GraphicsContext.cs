using System;
using System.Numerics;
using System.Runtime.InteropServices;
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

    /// <inheritdoc />
    public GraphicsDeviceInfo DeviceInfo { get; }

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

        // Read one-off device/driver strings from the current context and strip them down to pure
        // strings, so Core/upper layers never see GLEnum/StringName.
        DeviceInfo = new GraphicsDeviceInfo(
            GetGlString(_gl, GLEnum.Vendor),
            GetGlString(_gl, GLEnum.Renderer),
            GetGlString(_gl, GLEnum.Version),
            GetGlString(_gl, GLEnum.ShadingLanguageVersion));
    }

    /// <summary>
    /// Reads a null-terminated GL string (e.g. GL_VENDOR) and copies it into a managed string, so no
    /// native pointer or GL type ever leaves this type.
    /// </summary>
    private static unsafe string GetGlString(GL gl, GLEnum name)
    {
        byte* value = gl.GetString(name);
        return value == null
            ? string.Empty
            : Marshal.PtrToStringAnsi((nint)value) ?? string.Empty;
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
