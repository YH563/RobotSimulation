using System;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// 模型导入选项。
/// </summary>
public sealed record LoadOptions
{
    /// <summary>默认导入选项（不做任何翻转/缩放）。</summary>
    public static LoadOptions Default { get; } = new();

    /// <summary>
    /// 翻转贴图 UV 的 V 坐标（v → 1-v）。默认 false：UV 以文件原样写入 MeshData。
    /// 当纹理上下颠倒时置 true 重试即可，无需改文件。
    /// </summary>
    public bool FlipUvV { get; init; }

    /// <summary>
    /// 翻转三角形绕序。默认 false：保持文件绕序。
    /// 渲染端开启背面剔除后发现模型“反了”时置 true，不用改网格数据。
    /// </summary>
    public bool FlipWinding { get; init; }

    /// <summary>
    /// 顶点全局缩放系数（单位校正用，如毫米→米时填 0.001）。默认 null = 不缩放；
    /// URDF 的 mesh scale 属于 Transform 语义，不在此处烘焙。
    /// </summary>
    public float? GlobalScale { get; init; }
}
