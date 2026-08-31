using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 纹理类型
/// </summary>
public enum TextureType
{
    Albedo,
    Normal,
    Metallic,
    Roughness
}

/// <summary>
/// 材质，管理多种不同纹理
/// </summary>
public class Material : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<TextureType, Texture2D> _textures = new();
    private bool _disposed = false;
    public ShaderProgram Shader { get; private set; }
    
    // 纹理缺失时的默认属性
    public Vector4 BaseColor { get; set; } = new Vector4(1f, 1f, 1f, 1f);
    public float MetallicFactor { get; set; } = 0.0f;
    public float RoughnessFactor { get; set; } = 0.5f;

    public Material(ShaderProgram shader)
    {
        _gl = shader._gl;
        Shader = shader;
    }
    
    /// <summary>
    /// 设置纹理
    /// </summary>
    /// <param name="type">纹理类型</param>
    /// <param name="texture">纹理，为null时表示删除对应类型纹理</param>
    public void SetTexture(TextureType type, Texture2D texture)
    {
        if (texture == null)
            _textures.Remove(type);
        else
            _textures[type] = texture;
    }
    
    /// <summary>
    /// 应用材质
    /// </summary>
    public void Apply()
    {
        Shader.Use();

        // 绑定纹理并传递 uniform
        int unit = 0;
        foreach (var kv in _textures)
        {
            var texUnit = (TextureUnit)(TextureUnit.Texture0 + unit);
            kv.Value.Bind(texUnit);
            string uniformName = GetUniformName(kv.Key);
            Shader.SetUniform(uniformName, unit);
            // 告诉着色器这张纹理存在（1 存在，0 缺失）
            Shader.SetUniform($"uHas{uniformName.Substring(1)}", 1);
            unit++;
        }

        // 对于未设置的纹理类型，标记为缺失
        foreach (TextureType type in Enum.GetValues(typeof(TextureType)))
        {
            if (!_textures.ContainsKey(type))
            {
                string uniformName = GetUniformName(type);
                Shader.SetUniform($"uHas{uniformName.Substring(1)}", 0);
            }
        }

        // 传递后备值
        Shader.SetUniform("uBaseColor", BaseColor);
        Shader.SetUniform("uMetallicFactor", MetallicFactor);
        Shader.SetUniform("uRoughnessFactor", RoughnessFactor);
    }
    
    private string GetUniformName(TextureType type) => type switch
    {
        TextureType.Albedo => "uAlbedo",
        TextureType.Normal => "uNormal",
        TextureType.Metallic => "uMetallic",
        TextureType.Roughness => "uRoughness",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
    
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var tex in _textures.Values)
            tex.Dispose();
        _disposed = true;
    }
}