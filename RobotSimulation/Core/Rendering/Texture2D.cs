using Silk.NET.OpenGL;
using StbImageSharp;
using System;
using System.IO;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 颜色空间枚举
/// </summary>
public enum TextureColorSpace
{
    Srgb,  // 漫反射（Albedo）、UI 贴图（需要伽马校正）
    Linear,  // 法线、金属度、粗糙度、高度图（纯数值，不要校正）
}

/// <summary>
/// 2D纹理类，可以兼容不同类型的纹理
/// </summary>
public class Texture2D : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private bool _disposed = false;

    public Texture2D(GL gl, string filePath, bool generateMipmaps = true, TextureColorSpace colorSpace = TextureColorSpace.Srgb)
    {
        _gl = gl;
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Texture not found: {filePath}");
        // 图像翻转，由于opengl的原点位于左下方
        StbImage.stbi_set_flip_vertically_on_load(1); 

        ImageResult result = ImageResult.FromMemory(File.ReadAllBytes(filePath), ColorComponents.RedGreenBlueAlpha);
        _handle = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

        unsafe
        {
            fixed (byte* ptr = result.Data)
            {
                InternalFormat internalFormat = (colorSpace == TextureColorSpace.Srgb) ?  InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8;
                _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                    (uint)result.Width, (uint)result.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }
        
        // 设置纹理参数，是否产生Minimap
        if (generateMipmaps)
        {
            // 生成 Mipmap 链
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            
            // 现在用三线性过滤（远处模糊平滑，近处清晰）
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 
                (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 
                (int)TextureMagFilter.Linear);
        }
        else
        {
            // 不生成 Mipmap，使用普通线性过滤（适合 UI 或点云 Sprite）
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 
                (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 
                (int)TextureMagFilter.Linear);
        }
        // 环绕方式（重复/边缘钳制）
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }
    
    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose()
    {
        if (!_disposed && _handle != 0)
        {
            _gl.DeleteTexture(_handle);
            _disposed = true;
        }
    }
}