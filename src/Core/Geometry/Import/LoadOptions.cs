using System;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// Model import options.
/// </summary>
public sealed record LoadOptions
{
    /// <summary>Default import options (no flipping or scaling).</summary>
    public static LoadOptions Default { get; } = new();

    /// <summary>
    /// Flips the texture UV V coordinate (v → 1-v). Default false: UVs are written to MeshData
    /// as-is from the file. Set true to retry when textures appear upside-down, without editing the file.
    /// </summary>
    public bool FlipUvV { get; init; }

    /// <summary>
    /// Flips triangle winding. Default false: preserve the file's winding.
    /// Set true when the model appears "inside-out" with back-face culling enabled, without editing the mesh data.
    /// </summary>
    public bool FlipWinding { get; init; }

    /// <summary>
    /// Global scale factor for vertices (unit correction, e.g. 0.001 for mm→m). Default null = no scaling;
    /// URDF mesh scale is a Transform concern and is not baked in here.
    /// </summary>
    public float? GlobalScale { get; init; }
}
