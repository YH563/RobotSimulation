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

    /// <summary>Point light world position (i.e. Transform position).</summary>
    public Vector3 Position => Transform.Position;

    public Light(string? name = "Light") : base(null, null, name)
    {
    }
}
