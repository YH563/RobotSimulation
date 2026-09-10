using System;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Renderer (public interface layer): draws a scene into the current rendering context.
/// The implementation layer (e.g. RobotSimulation.OpenGL.Rendering.Renderer) owns the GPU resource
/// lifecycle internally; callers depend only on this interface and the pure-data scene
/// (SceneGraph/GameObject/MeshData/MaterialData). <see cref="Render"/> must be called on the render thread.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Draws the entire scene.</summary>
    void Render(SceneGraph scene);

    /// <summary>
    /// Latest frame timing statistics (FPS / frame time), updated by the backend on each
    /// <see cref="Render"/> call. Pure data, so the host can display a performance overlay.
    /// </summary>
    FrameStats Stats { get; }
}
