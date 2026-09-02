namespace RobotSimulation.Core.GameObjects;

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
/// </summary>
/// <param name="FilePath">贴图文件路径。</param>
/// <param name="ColorSpace">贴图颜色空间（决定解码/上传时的伽马处理）。</param>
/// <param name="GenerateMipmaps">是否生成 mipmap 链。</param>
public sealed record TextureReference(
    string FilePath,
    TextureColorSpace ColorSpace = TextureColorSpace.Srgb,
    bool GenerateMipmaps = true);
