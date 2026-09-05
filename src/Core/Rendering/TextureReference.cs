namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 贴图颜色空间语义（纯数据，与具体渲染 API 无关）。
/// Srgb：漫反射（Albedo）/UI 贴图，上传后需伽马校正；
/// Linear：法线/金属度/粗糙度等数值贴图，不做颜色校正。
/// </summary>
public enum TextureColorSpace
{
    Srgb,
    Linear,
}

/// <summary>
/// CPU 侧贴图引用：只描述"用哪个文件、以什么语义上传"，不持有任何 GPU 状态。
/// 具体的解码与 GPU 上传由渲染后端（如 RobotSimulation.OpenGL.Texture2D）完成。
/// 从文件载入贴图与直接载入原始数据只能选择一种，便于兼容3D模型文件的直接读取
/// </summary>
/// <param name="FilePath">贴图文件路径。</param>
/// <param name="ImageData">贴图原始字节数据</param>
/// <param name="ColorSpace">贴图颜色空间（决定解码/上传时的伽马处理）。</param>
/// <param name="GenerateMipmaps">是否生成 mipmap 链。</param>
public sealed record TextureReference(
    string? FilePath = null,
    byte[]? ImageData = null,
    TextureColorSpace ColorSpace = TextureColorSpace.Srgb,
    bool GenerateMipmaps = true
) {
    // 检验数据正确性
    public bool IsValid => string.IsNullOrEmpty(FilePath) || (ImageData is { Length: > 0 });
    
    public bool HasFileData => !string.IsNullOrEmpty(FilePath);
    public bool HasMemoryData => ImageData is { Length: > 0 };
    
    /// <summary>从文件加载贴图</summary>
    public static TextureReference FromFile(string filePath, 
        TextureColorSpace colorSpace = TextureColorSpace.Srgb, 
        bool generateMipmaps = true)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        return new TextureReference(filePath, null, colorSpace, generateMipmaps);
    }
    
    /// <summary> 从内存字节数据创建 </summary>
    public static TextureReference FromData(byte[] data, 
        TextureColorSpace colorSpace = TextureColorSpace.Srgb, 
        bool generateMipmaps = true)
    {
        if (data is null or { Length: 0 })
            throw new ArgumentException("Image data cannot be null or empty.", nameof(data));
        return new TextureReference(null, data, colorSpace, generateMipmaps);
    }
}
