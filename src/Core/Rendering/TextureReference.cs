namespace RobotSimulation.Core.Rendering;

/// <summary>
/// Color-space semantics of a texture (pure data, independent of any rendering API).
/// Srgb: albedo / UI textures, gamma-corrected on upload.
/// Linear: normal / metallic / roughness numeric textures, no color correction.
/// </summary>
public enum TextureColorSpace
{
    /// <summary>Gamma-corrected color texture (albedo, UI); the backend uploads it as an sRGB internal format.</summary>
    Srgb,

    /// <summary>Raw numeric texture (normal / metallic / roughness maps); no color correction on upload.</summary>
    Linear,
}

/// <summary>
/// CPU-side texture reference: it only describes "which file and with what semantics to
/// upload" and holds no GPU state. Actual decoding and GPU upload are done by the rendering
/// backend (e.g. RobotSimulation.OpenGL.Texture2D).
/// A texture is created from either a file or raw in-memory data, never both.
/// </summary>
/// <param name="FilePath">Texture file path.</param>
/// <param name="ImageData">Raw texture bytes.</param>
/// <param name="ColorSpace">Texture color space (controls gamma handling on decode/upload).</param>
/// <param name="GenerateMipmaps">Whether to generate a mipmap chain.</param>
public sealed record TextureReference(
    string? FilePath = null,
    byte[]? ImageData = null,
    TextureColorSpace ColorSpace = TextureColorSpace.Srgb,
    bool GenerateMipmaps = true
) {
    // Valid only when the reference was populated from a file or from memory.
    /// <summary>Whether at least one source is set (a file path or in-memory bytes); otherwise the backend rejects it.</summary>
    public bool IsValid => HasFileData || HasMemoryData;

    /// <summary>Whether this reference points at a texture file on disk.</summary>
    public bool HasFileData => !string.IsNullOrEmpty(FilePath);

    /// <summary>Whether this reference carries raw encoded image bytes.</summary>
    public bool HasMemoryData => ImageData is { Length: > 0 };

    /// <summary>Creates a texture reference backed by a file path.</summary>
    public static TextureReference FromFile(string filePath,
        TextureColorSpace colorSpace = TextureColorSpace.Srgb,
        bool generateMipmaps = true)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        return new TextureReference(filePath, null, colorSpace, generateMipmaps);
    }

    /// <summary>Creates a texture reference backed by raw in-memory bytes.</summary>
    public static TextureReference FromData(byte[] data,
        TextureColorSpace colorSpace = TextureColorSpace.Srgb,
        bool generateMipmaps = true)
    {
        if (data is null or { Length: 0 })
            throw new ArgumentException("Image data cannot be null or empty.", nameof(data));
        return new TextureReference(null, data, colorSpace, generateMipmaps);
    }
}
