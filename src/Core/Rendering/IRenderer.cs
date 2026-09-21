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
    /// <summary>
    /// Draws the entire scene. This is the scene's frame boundary: an implementation applies the structural
    /// changes other threads queued through <see cref="SceneGraph.Add"/> / <see cref="SceneGraph.Remove"/>
    /// (and re-parents) here (<see cref="SceneGraph.ApplyPendingChanges"/>), so a host can keep building the
    /// scene from a worker thread and see the objects appear, at the latest, in the frame after they were
    /// requested. Must be called on the render thread, and that thread must be the scene's owner thread — a
    /// host that constructs its scene inside the frame loop satisfies this by construction, and any other host
    /// hands the scene over with <see cref="SceneGraph.ClaimOwnership"/> before the loop starts. The boundary
    /// throws <see cref="InvalidOperationException"/> rather than taking ownership from the caller, because the
    /// render thread and "the thread allowed to write the scene's lists" have to stay one thread.
    /// </summary>
    void Render(SceneGraph scene);

    /// <summary>
    /// Latest frame timing statistics (FPS / frame time), updated by the backend on each
    /// <see cref="Render"/> call. Pure data, so the host can display a performance overlay.
    /// </summary>
    FrameStats Stats { get; }
}
