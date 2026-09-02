using RobotSimulation.Core.GameObjects;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>纹理类型。</summary>
public enum TextureType
{
    Albedo,
    Normal,
    Metallic,
    Roughness
}

/// <summary>
/// GPU 侧材质：持有着色器与已上传纹理，绘制前把 CPU 侧外观参数
/// （<see cref="MaterialData"/>）写入 uniform。
///
/// 注意：本类不复制 <see cref="MaterialData"/> 的外观字段——那会形成两套
/// 相同数据并迫使调用方每帧手动同步。外观参数一律以 <see cref="Apply"/> 的参数为准。
/// </summary>
public class Material : IDisposable
{
    private readonly Dictionary<TextureType, Texture2D> _textures = new();
    private bool _disposed = false;

    public ShaderProgram Shader { get; }

    public Material(ShaderProgram shader)
    {
        Shader = shader;
    }

    /// <summary>设置纹理；传 null 表示移除对应类型纹理。</summary>
    public void SetTexture(TextureType type, Texture2D texture)
    {
        if (texture == null)
            _textures.Remove(type);
        else
            _textures[type] = texture;
    }

    /// <summary>
    /// 应用材质：绑定着色器与纹理，并把 <paramref name="appearance"/> 的外观参数写入 uniform。
    /// 应在每帧、每个使用该材质的对象绘制前调用（外观数据可被任意线程修改，绘制前取最新值）。
    /// </summary>
    public void Apply(MaterialData appearance)
    {
        Shader.Use();

        // 绑定纹理并传递 uniform；未设置的纹理类型标记为缺失（0）
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

        // 外观参数（直接从 CPU 描述读取，无本地副本）
        Shader.SetUniform("uBaseColor", appearance.BaseColor);
        Shader.SetUniform("uMetallicFactor", appearance.MetallicFactor);
        Shader.SetUniform("uRoughnessFactor", appearance.RoughnessFactor);
    }

    /// <summary>uAlbedo → uHasAlbedo（约定 uniform 名以 "u" 开头、后接大写属性名）。</summary>
    private static string HasFlagName(string uniformName) => "uHas" + uniformName[1..];

    private static string GetUniformName(TextureType type) => type switch
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
        _textures.Clear();
        _disposed = true;
    }
}
