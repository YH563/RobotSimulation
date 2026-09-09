using System;
using System.Numerics;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Rendering context (public interface layer): viewport management, clearing, and other device
/// capabilities independent of a specific graphics API. The implementation layer (e.g.
/// RobotSimulation.OpenGL.GraphicsContext) wraps a concrete GL context; callers depend only on this
/// interface and never touch Silk/GL types. Colors are uniformly RGBA with components in [0,1]
/// (Vector4), avoiding platform types like System.Drawing.
/// </summary>
public interface IRenderContext : IDisposable
{
    /// <summary>Raised when the viewport size changes; the new width and height are passed.</summary>
    event Action<int, int>? Resized;

    /// <summary>Updates the viewport size (usually called by the host on window resize, also raising <see cref="Resized"/>).</summary>
    void Resize(int width, int height);

    /// <summary>
    /// Clears the color buffer (and optionally the depth buffer).
    /// </summary>
    /// <param name="clearColor">Clear color RGBA, components in [0,1].</param>
    /// <param name="clearDepth">Whether to also clear the depth buffer, default true.</param>
    void Clear(Vector4 clearColor, bool clearDepth = true);
}
