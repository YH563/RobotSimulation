using System;
using RobotSimulation.Core.Rendering;
using RobotSimulation.OpenGL.Rendering;
using Silk.NET.OpenGL;

namespace RobotSimulation.OpenGL.Device;

/// <summary>
/// Composition root for the OpenGL backend, so an embedding host can assemble the rendering pipeline from
/// a raw GL instance via a single entry point without touching concrete backend types. The returned
/// <see cref="IRenderContext"/> and <see cref="IRenderer"/> are the only things the host should hold;
/// GL does not propagate outward.
/// </summary>
public static class GraphicsFactory
{
    /// <summary>Wraps a GL instance into an <see cref="IRenderContext"/> (the device layer).</summary>
    public static IRenderContext CreateContext(GL gl)
        => new GraphicsContext(gl ?? throw new ArgumentNullException(nameof(gl)));

    /// <summary>Creates a renderer bound to the given device context.</summary>
    public static IRenderer CreateRenderer(IRenderContext context)
        => new Renderer(context as GraphicsContext
            ?? throw new ArgumentException("The context must be a RobotSimulation.OpenGL.GraphicsContext.", nameof(context)));

    /// <summary>
    /// Creates a device context and its renderer from a GL instance in one call, the usual composition
    /// entry for hosts that want to render a scene immediately.
    /// </summary>
    public static (IRenderContext Context, IRenderer Renderer) Create(GL gl)
    {
        var context = new GraphicsContext(gl ?? throw new ArgumentNullException(nameof(gl)));
        return (context, new Renderer(context));
    }
}
