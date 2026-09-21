using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>Light type.</summary>
public enum LightType
{
    /// <summary>Point light: radiates in all directions; position from Transform.Position.</summary>
    Point,

    /// <summary>Directional light: models an infinitely far source (e.g. the sun), shining along one direction, position-independent.</summary>
    Directional,
}

/// <summary>
/// Scene light (a special GameObject). Carries no Mesh/Material, so it is not ordinarily drawn; the
/// renderer collects its parameters (type/color/intensity/position) each frame and passes them to the
/// shader. Scenes support multiple lights (see <see cref="SceneGraph.Lights"/>).
/// </summary>
public class Light : GameObject
{
    /// <summary>Light type.</summary>
    public LightType Type { get; set; } = LightType.Point;

    /// <summary>Light color (RGB, components in [0,1]; may be &gt;1 for highlight).</summary>
    public Vector3 Color { get; set; } = Vector3.One;

    /// <summary>Light intensity factor.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>Effective light radius (for point-light attenuation; &lt;=0 means no attenuation).</summary>
    public float Range { get; set; }

    /// <summary>Directional light's lighting direction (world coordinates, pointing along the light); ignored for point lights.</summary>
    public Vector3 Direction { get; set; } = -Vector3.UnitZ;

    /// <summary>
    /// Point light position in its parent's space — for the usual scene-root light this is the world position,
    /// as with any other node's <see cref="Transform.Position"/>.
    /// </summary>
    public Vector3 Position => Transform.Position;

    /// <summary>
    /// Point light world position: <see cref="Position"/> composed with every ancestor transform, so a light
    /// parented to something (a lamp mounted on a robot) lights from where it really is instead of from a local
    /// offset. This is what the renderer feeds to the shader.
    /// </summary>
    public Vector3 WorldPosition => Transform.GetModelMatrix().Translation;

    /// <summary>
    /// Directional light world direction: <see cref="Direction"/> carried through the transform chain and
    /// re-normalized (a scaled parent must not stretch it). This is what the renderer feeds to the shader.
    /// </summary>
    public Vector3 WorldDirection
    {
        get
        {
            Vector3 world = Vector3.TransformNormal(Direction, Transform.GetModelMatrix());
            return world.LengthSquared() > 0f ? Vector3.Normalize(world) : Direction;
        }
    }

    /// <summary>Creates a light with the default appearance (white, intensity 1, point type).</summary>
    /// <param name="name">Scene object name (defaults to <c>Light</c>).</param>
    public Light(string? name = "Light") : base(null, null, name)
    {
    }
}
