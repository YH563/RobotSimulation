namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Graphics device / driver information exposed by a rendering context. Pure data (all strings, no
/// graphics-API types): the backend reads it from the driver, upper layers can only display or log it.
/// Useful for support and diagnostics ("which GPU/driver is this running on?").
/// </summary>
/// <param name="Vendor">Device vendor string (e.g. the GL_VENDOR equivalent).</param>
/// <param name="Renderer">Renderer/GPU name string (e.g. the GL_RENDERER equivalent).</param>
/// <param name="ApiVersion">Graphics API version string (e.g. the GL_VERSION equivalent).</param>
/// <param name="ShaderVersion">Shading language version string (e.g. the GL_SHADING_LANGUAGE_VERSION equivalent).</param>
public readonly record struct GraphicsDeviceInfo(
    string Vendor,
    string Renderer,
    string ApiVersion,
    string ShaderVersion)
{
    /// <summary>Unknown device information (used when a backend cannot report it).</summary>
    public static readonly GraphicsDeviceInfo Unknown =
        new(string.Empty, string.Empty, string.Empty, string.Empty);
}
