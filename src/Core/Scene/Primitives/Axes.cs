using System;
using System.Numerics;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Coordinate axes (<see cref="GameObject"/>, Model pass): three solid RGB arrows at the origin —
/// +X red, +Y green, +Z blue (rviz style, more visible than thin lines). The whole set can be
/// placed/rotated via its <see cref="Transform"/>.
/// </summary>
public sealed class Axes : GameObject
{
    private static readonly Quaternion ToX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
    private static readonly Quaternion ToY = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    /// <summary>Arrow length (how far each axis extends from the origin).</summary>
    public float Length { get; }

    // ---- Constant screen size parameters (managed internally by the axes; the Renderer only reads) ----

    /// <summary>Constant screen size factor: target world length = <see cref="ScreenScale"/> × camera-to-origin distance.</summary>
    public const float DefaultScreenScale = 0.08f;

    /// <summary>Minimum world length of each axis.</summary>
    public const float DefaultMinLength = 0.15f;

    /// <summary>Maximum world length of each axis.</summary>
    public const float DefaultMaxLength = 1.5f;

    /// <summary>Constant screen size factor (adjust here to tune axis size/feel; the Renderer does not own this constant).</summary>
    public float ScreenScale { get; set; } = DefaultScreenScale;

    /// <summary>Minimum world length of each axis (never smaller when the camera is close).</summary>
    public float MinWorldLength { get; set; } = DefaultMinLength;

    /// <summary>Maximum world length of each axis (never larger when the camera is far).</summary>
    public float MaxWorldLength { get; set; } = DefaultMaxLength;

    /// <summary>Creates the three axis arrows as children of this node (+X red, +Y green, +Z blue).</summary>
    /// <param name="length">Initial arrow length.</param>
    /// <param name="shaftRadius">Radius of each arrow's shaft.</param>
    /// <param name="headRadius">Radius of each arrow's cone head.</param>
    /// <param name="headLength">Length of each arrow's cone head.</param>
    /// <param name="name">Scene object name (defaults to <c>Axes</c>).</param>
    public Axes(
        float length = 1.2f,
        float shaftRadius = 0.01f,
        float headRadius = 0.03f,
        float headLength = 0.1f,
        string? name = null)
        : base(null, null, name ?? nameof(Axes))
    {
        if (length <= 0f || float.IsNaN(length) || float.IsInfinity(length))
            throw new ArgumentOutOfRangeException(nameof(length), length, "Axis length must be a positive finite value.");
        Length = length;

        var x = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.90f, 0.20f, 0.20f, 1f), 24, "axes_x")
        {
            Transform = { Rotation = ToX },
        };
        var y = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.25f, 0.85f, 0.30f, 1f), 24, "axes_y")
        {
            Transform = { Rotation = ToY },
        };
        var z = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.25f, 0.45f, 1f, 1f), 24, "axes_z");

        // Axis arrows go through the dedicated "constant size" pass (shader screen compensation), not the normal lit model pass.
        x.MaterialData!.PassKind = RenderPassKind.Axes;
        y.MaterialData!.PassKind = RenderPassKind.Axes;
        z.MaterialData!.PassKind = RenderPassKind.Axes;

        x.Transform.Parent = Transform;
        y.Transform.Parent = Transform;
        z.Transform.Parent = Transform;
    }
}
