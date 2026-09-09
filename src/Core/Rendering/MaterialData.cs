using System.Numerics;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Material description (CPU data), attached to a GameObject to describe appearance and render state.
/// It contains no GPU/shader state — the concrete GPU material instantiation and texture upload are
/// done by the rendering backend at render time (this type only holds "file path / color space"
/// references and can be created or modified on any thread).
/// </summary>
public sealed class MaterialData
{
    // ---- PBR appearance ----

    /// <summary>RGBA base color, components in [0,1]. ↔ rendering backend's uBaseColor.</summary>
    public Vector4 BaseColor { get; set; } = new Vector4(1f, 1f, 1f, 1f);

    /// <summary>Metallic factor ([0,1]).</summary>
    public float MetallicFactor { get; set; }

    /// <summary>Roughness factor ([0,1]).</summary>
    public float RoughnessFactor { get; set; } = 0.5f;

    // ---- Texture references (CPU descriptions; GPU upload is the rendering backend's job) ----

    /// <summary>Albedo/diffuse texture. ↔ rendering backend's Albedo texture slot.</summary>
    public TextureReference? AlbedoTexture { get; set; }

    /// <summary>Normal texture. ↔ rendering backend's Normal texture slot.</summary>
    public TextureReference? NormalTexture { get; set; }

    /// <summary>Metallic texture (R channel). ↔ rendering backend's Metallic texture slot.</summary>
    public TextureReference? MetallicTexture { get; set; }

    /// <summary>Roughness texture (G channel). ↔ rendering backend's Roughness texture slot.</summary>
    public TextureReference? RoughnessTexture { get; set; }

    // ---- Render state bits ----

    /// <summary>Whether to render double-sided (true = back-face culling off). Some URDF/OBJ models are not closed.</summary>
    public bool DoubleSided { get; set; }

    /// <summary>Whether to draw in wireframe mode.</summary>
    public bool Wireframe { get; set; }

    /// <summary>Render pass kind (default is the Model pass). Line/Point passes are used for grid floors, axes, point clouds, etc.</summary>
    public RenderPassKind PassKind { get; set; } = RenderPassKind.Model;
}
