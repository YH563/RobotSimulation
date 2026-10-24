namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Render pass: determines which shading effect / draw method a drawable object uses.
/// Core only defines "which passes exist" (the effect catalog); each pass's default GLSL
/// implementation is provided by the rendering backend (RobotSimulation.OpenGL's EmbeddedShaders),
/// so the host does not manage shader resources itself.
/// </summary>
public enum RenderPassKind
{
    /// <summary>Opaque meshes (triangle lists, PBR / Blinn-Phong lighting).</summary>
    Model = 0,

    /// <summary>Unlit lines (grid floor, axes, path previews, etc.).</summary>
    Line = 1,

    /// <summary>Unlit point set (point clouds, etc.).</summary>
    Point = 2,

    /// <summary>Environment skybox (scene-level background, not an ordinary node draw).</summary>
    Skybox = 3,

    /// <summary>
    /// Axis-dedicated pass: solid-color arrows where the vertex shader does a "constant screen size"
    /// compensation — only the world orientation/position of the axis is kept, and the visual length
    /// neither grows with parent scale nor shrinks with camera distance.
    /// </summary>
    Axes = 4,

    /// <summary>
    /// Unlit translucent debug fill (collision volumes, trigger zones). These nodes are drawn after the
    /// opaque scene with alpha blending and no depth test, so a debug volume stays visible through the
    /// geometry it annotates. Intended for diagnostics, not for product geometry.
    /// </summary>
    Debug = 5,
}
