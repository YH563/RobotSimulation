using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// Standard shader catalog: the GLSL source files live in this assembly's <c>Shaders/</c> directory
/// (Model/Line/Point/Skybox/Axes each with a .vert/.frag), distributed as embedded resources with
/// RobotSimulation.OpenGL. The Core side only agrees on the <see cref="RenderPassKind"/> passes; the
/// host needs no shader file paths. Edit GLSL directly under <c>Shaders/*.vert|frag</c>.
/// </summary>
public static class EmbeddedShaders
{
    private static readonly Assembly Self = typeof(EmbeddedShaders).Assembly;

    /// <summary>Gets the (vertex, fragment) sources for a pass.</summary>
    public static (string Vertex, string Fragment) Get(RenderPassKind pass) => pass switch
    {
        RenderPassKind.Model => (Read("Model.vert"), Read("Model.frag")),
        RenderPassKind.Line => (Read("Line.vert"), Read("Line.frag")),
        RenderPassKind.Point => (Read("Point.vert"), Read("Point.frag")),
        RenderPassKind.Skybox => (Read("Skybox.vert"), Read("Skybox.frag")),
        RenderPassKind.Axes => (Read("Axes.vert"), Read("Axes.frag")),
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    /// <summary>Reads the embedded GLSL text (resource names end with ".Shaders.{fileName}", ignoring any assembly-root namespace difference).</summary>
    private static string Read(string fileName)
    {
        string suffix = $".Shaders.{fileName}";
        string? resourceName = Self.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));

        if (resourceName is null)
            throw new FileNotFoundException(
                $"Embedded shader resource '{suffix}' not found." +
                "Confirm the .vert/.frag files under RobotSimulation.OpenGL's Shaders/ directory are included as EmbeddedResource.");

        using Stream stream = Self.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
