using System.Numerics;

namespace RobotSimulation.Core.GameObjects;

/// <summary>
/// 材质描述（CPU 侧数据），挂在 GameObject 上描述外观与渲染状态。
/// 不包含任何 GPU/着色器状态——具体的 GPU 材质实例化与贴图上传由渲染后端在渲染时完成
/// （本类型只持有"文件路径/颜色空间"级别的引用，可在任意线程创建与修改）。
/// </summary>
public sealed class MaterialData
{
    // ---- PBR 外观 ----

    /// <summary>RGBA 主色，分量范围 [0,1]。↔ 渲染后端的 uBaseColor。</summary>
    public Vector4 BaseColor { get; set; } = new Vector4(1f, 1f, 1f, 1f);

    /// <summary>金属度系数（[0,1]）。</summary>
    public float MetallicFactor { get; set; }

    /// <summary>粗糙度系数（[0,1]）。</summary>
    public float RoughnessFactor { get; set; } = 0.5f;

    // ---- 贴图引用（CPU 描述；GPU 上传归渲染后端）----

    /// <summary>反照率/漫反射贴图。↔ 渲染后端的 Albedo 纹理槽。</summary>
    public TextureReference? AlbedoTexture { get; set; }

    /// <summary>法线贴图。↔ 渲染后端的 Normal 纹理槽。</summary>
    public TextureReference? NormalTexture { get; set; }

    /// <summary>金属度贴图（R 通道）。↔ 渲染后端的 Metallic 纹理槽。</summary>
    public TextureReference? MetallicTexture { get; set; }

    /// <summary>粗糙度贴图（G 通道）。↔ 渲染后端的 Roughness 纹理槽。</summary>
    public TextureReference? RoughnessTexture { get; set; }

    // ---- 渲染状态位 ----

    /// <summary>是否双面渲染（true = 关闭背面剔除）。URDF/OBJ 有时为无闭合面模型所需。</summary>
    public bool DoubleSided { get; set; }

    /// <summary>是否以线框模式绘制。</summary>
    public bool Wireframe { get; set; }
}

