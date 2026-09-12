using System;
using System.IO;
using RobotSimulation.Core.Rendering;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// A GPU-backed 2D texture. It can be created from either a file path or raw in-memory
/// bytes via a <see cref="TextureReference"/>.
/// Color-space semantics follow <see cref="TextureColorSpace"/> (Srgb requires gamma
/// correction; Linear is treated as raw numeric data).
/// </summary>
public class Texture2D : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private bool _disposed;

    /// <summary>Decodes and uploads the texture described by <paramref name="textureRef"/>.</summary>
    /// <param name="gl">The GL facade to create the texture on.</param>
    /// <param name="textureRef">CPU-side reference (file path or raw bytes) plus color-space/mipmap settings.</param>
    /// <exception cref="ArgumentException"><paramref name="textureRef"/> is invalid (neither a path nor data).</exception>
    /// <exception cref="System.IO.FileNotFoundException">The referenced file does not exist.</exception>
    public Texture2D(GL gl, TextureReference textureRef)
    {
        _gl = gl;
        if (!textureRef.IsValid)
            throw new ArgumentException("TextureReference is invalid (no file path or data).");

        // 1. Resolve the source bytes (from file, or use the provided memory data directly).
        byte[] imageBytes;
        if (textureRef.HasMemoryData)
        {
            imageBytes = textureRef.ImageData!;
        }
        else // HasFileData
        {
            if (!File.Exists(textureRef.FilePath))
                throw new FileNotFoundException($"Texture file not found: {textureRef.FilePath}");
            imageBytes = File.ReadAllBytes(textureRef.FilePath!);
        }

        // 2. Flip vertically and decode (OpenGL's origin is bottom-left).
        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult result = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);

        // 3. Upload to the GPU using the reference's properties.
        _handle = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);

        unsafe
        {
            fixed (byte* ptr = result.Data)
            {
                InternalFormat internalFormat = textureRef.ColorSpace == TextureColorSpace.Srgb
                    ? InternalFormat.Srgb8Alpha8
                    : InternalFormat.Rgba8;
                _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                    (uint)result.Width, (uint)result.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        // 4. Set sampling parameters based on GenerateMipmaps.
        if (textureRef.GenerateMipmaps)
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
        }
        else
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <summary>Convenience overload that uploads a texture straight from a file path.</summary>
    /// <param name="gl">The GL facade to create the texture on.</param>
    /// <param name="filePath">Image file to decode.</param>
    /// <param name="generateMipmaps">Whether to build a mipmap chain.</param>
    /// <param name="colorSpace">Srgb for color textures, Linear for numeric maps.</param>
    public Texture2D(GL gl, string filePath, bool generateMipmaps = true, TextureColorSpace colorSpace = TextureColorSpace.Srgb)
        : this(gl, TextureReference.FromFile(filePath, colorSpace, generateMipmaps))
    {
    }

    /// <summary>Makes this texture current on the given texture unit (sampler value), ready to be sampled.</summary>
    /// <param name="unit">Target texture unit (default <c>Texture0</c>).</param>
    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    /// <summary>Deletes the GL texture; safe to call more than once.</summary>
    public void Dispose()
    {
        if (!_disposed && _handle != 0)
        {
            _gl.DeleteTexture(_handle);
            _disposed = true;
        }
    }
}
