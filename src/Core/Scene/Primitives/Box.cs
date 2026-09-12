using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Box primitive (<see cref="GameObject"/> derived): constructs a local-space axis-aligned box mesh.
/// The size is the full width along x/y/z (URDF &lt;box size&gt; semantics); no revolution/axis involved.
/// </summary>
public sealed class Box : GameObject
{
    /// <summary>Full width along X.</summary>
    public float Width { get; }

    /// <summary>Full width along Y.</summary>
    public float Height { get; }

    /// <summary>Full width along Z.</summary>
    public float Depth { get; }

    /// <summary>Creates a box mesh object centered on the local origin.</summary>
    /// <param name="width">Full width along X.</param>
    /// <param name="height">Full width along Y.</param>
    /// <param name="depth">Full width along Z.</param>
    /// <param name="name">Scene object name (defaults to <c>Box</c>).</param>
    public Box(float width, float height, float depth, string? name = null)
        : base(Primitives.CreateBox(width, height, depth), new MaterialData(), name ?? nameof(Box))
    {
        Width = width;
        Height = height;
        Depth = depth;
    }
}
