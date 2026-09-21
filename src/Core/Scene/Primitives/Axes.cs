using System;
using System.Numerics;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// How an <see cref="Axes"/> set decides the world length of its arrows on every frame.
/// </summary>
public enum AxesSizing
{
    /// <summary>
    /// Keep the axes the same size on screen whatever the camera distance (RViz-style screen compensation).
    /// Good for a small per-object marker, useless as a ruler: the world length then says nothing.
    /// </summary>
    ConstantScreenSize,

    /// <summary>
    /// Pin the arrows to <see cref="Axes.Length"/> world units. The set behaves like a ruler in the scene:
    /// it can be made to stand out beyond the model, and it shrinks on screen when the camera moves away.
    /// </summary>
    FixedWorldLength,
}

/// <summary>
/// Coordinate axes (<see cref="GameObject"/>, Model pass): three solid RGB arrows at the origin —
/// +X red, +Y green, +Z blue (rviz style, more visible than thin lines). The whole set can be
/// placed/rotated via its <see cref="Transform"/>.
/// </summary>
public sealed class Axes : GameObject
{
    private static readonly Quaternion ToX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
    private static readonly Quaternion ToY = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    /// <summary>
    /// Visible world length of each axis (how far each arrow extends from the origin). In
    /// <see cref="AxesSizing.FixedWorldLength"/> this is exactly what the renderer draws; in
    /// <see cref="AxesSizing.ConstantScreenSize"/> the drawn length follows the camera distance and this
    /// value only fixes the arrow geometry's reference length. Setting it re-scales the whole set: the axes
    /// shader normalises by the arrow's own length, so no geometry has to be rebuilt.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public float Length
    {
        get => _length;
        set
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Axis length must be a positive finite value.");
            _length = value;
        }
    }

    private float _length;

    /// <summary>
    /// How the visible length is decided. <see cref="AxesSizing.ConstantScreenSize"/> (the default) keeps a
    /// marker legible at any zoom; <see cref="AxesSizing.FixedWorldLength"/> makes the set a scene-space
    /// reference instead — see <see cref="SceneGraph.FitWorldAxesToContent"/> for sizing it past the model.
    /// </summary>
    public AxesSizing Sizing { get; set; } = AxesSizing.ConstantScreenSize;

    /// <summary>
    /// Whether the set is an annotation no geometry may hide: the renderer leaves such a set (and its whole
    /// subtree) out of the ordinary, depth-tested walk, clears the depth buffer once the scene is drawn, and only
    /// then draws it — so a marker that sits <em>inside</em> the mesh it annotates stays readable instead of being
    /// half buried in it. The on-top sets are still depth-tested against each other, so a nearer marker correctly
    /// wins where two overlap.
    /// <para>
    /// Default false, deliberately: a <see cref="AxesSizing.FixedWorldLength"/> set is a ruler, and a ruler that
    /// shows through the model it measures lies about the scene. The per-object frame marker
    /// (<see cref="GameObject.ShowLocalAxes"/>, which is also what <see cref="SceneGraph.Select"/> mounts on a
    /// pick) turns it on when it creates its set — set it back to false on <see cref="GameObject.LocalAxes"/> to
    /// get an ordinary, occludable set.
    /// </para>
    /// </summary>
    public bool AlwaysOnTop { get; set; }

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
        _length = length;   // Already validated above; the property setter's own check would only rename the parameter.

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

        // Axis arrows go through the dedicated axes pass (which stretches them to whatever length the owning
        // Axes asks for), not the normal lit model pass.
        x.MaterialData!.PassKind = RenderPassKind.Axes;
        y.MaterialData!.PassKind = RenderPassKind.Axes;
        z.MaterialData!.PassKind = RenderPassKind.Axes;

        x.Transform.Parent = Transform;
        y.Transform.Parent = Transform;
        z.Transform.Parent = Transform;
    }
}
