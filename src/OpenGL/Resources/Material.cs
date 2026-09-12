using RobotSimulation.Core.Rendering;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>Texture type.</summary>
public enum TextureType
{
    /// <summary>Base color / diffuse map (sRGB).</summary>
    Albedo,

    /// <summary>Tangent-space normal map (linear).</summary>
    Normal,

    /// <summary>Metallic map (linear).</summary>
    Metallic,

    /// <summary>Roughness map (linear).</summary>
    Roughness
}

/// <summary>
/// GPU-side material: owns the shader and uploaded textures, and before drawing writes the CPU-side
/// appearance parameters (<see cref="MaterialData"/>) into uniforms.
///
/// Note: this class does not copy the appearance fields of <see cref="MaterialData"/> — that would create
/// two sets of the same data and force the caller to manually sync each frame. Appearance parameters are
/// always taken from the arguments to <see cref="Apply"/>.
/// </summary>
public class Material : IDisposable
{
    private readonly Dictionary<TextureType, Texture2D> _textures = new();
    private bool _disposed = false;

    /// <summary>Shader program shared by every draw call that uses this material (owned by the caller, not disposed here).</summary>
    public ShaderProgram Shader { get; }

    /// <summary>Wraps an already-compiled shader program.</summary>
    /// <param name="shader">The program this material draws with.</param>
    public Material(ShaderProgram shader)
    {
        Shader = shader;
    }

    /// <summary>Sets a texture; passing null removes the texture of that type.</summary>
    public void SetTexture(TextureType type, Texture2D texture)
    {
        if (texture == null)
            _textures.Remove(type);
        else
            _textures[type] = texture;
    }

    /// <summary>
    /// Applies the material: binds the shader and textures, and writes <paramref name="appearance"/>'s
    /// appearance parameters into uniforms. Call before drawing each object using this material (the
    /// appearance data can be modified from any thread, so read the latest value before drawing).
    /// </summary>
    public void Apply(MaterialData appearance)
    {
        Shader.Use();

        // Bind textures and pass the uniform; mark unset texture types as absent (0).
        int unit = 0;
        foreach (var (type, texture) in _textures)
        {
            var texUnit = (TextureUnit)(TextureUnit.Texture0 + unit);
            texture.Bind(texUnit);
            string uniformName = GetUniformName(type);
            Shader.SetUniform(uniformName, unit);
            Shader.SetUniform(HasFlagName(uniformName), 1);
            unit++;
        }

        foreach (TextureType type in Enum.GetValues(typeof(TextureType)))
        {
            if (_textures.ContainsKey(type)) continue;
            Shader.SetUniform(HasFlagName(GetUniformName(type)), 0);
        }

        // Appearance parameters (read directly from the CPU description, no local copy).
        Shader.SetUniform("uBaseColor", appearance.BaseColor);
        Shader.SetUniform("uMetallicFactor", appearance.MetallicFactor);
        Shader.SetUniform("uRoughnessFactor", appearance.RoughnessFactor);
    }

    /// <summary>uAlbedo → uHasAlbedo (convention: uniform names start with "u" followed by the capitalized property name).</summary>
    private static string HasFlagName(string uniformName) => "uHas" + uniformName[1..];

    private static string GetUniformName(TextureType type) => type switch
    {
        TextureType.Albedo => "uAlbedo",
        TextureType.Normal => "uNormal",
        TextureType.Metallic => "uMetallic",
        TextureType.Roughness => "uRoughness",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Disposes every texture this material owns (the shader is not disposed here); safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var tex in _textures.Values)
            tex.Dispose();
        _textures.Clear();
        _disposed = true;
    }
}
