namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 渲染通道：决定一个可绘制对象用哪一类着色效果 / 绘制方式。
/// Core 只定义"有哪些通道"（效果目录）；每个通道的 GLSL 默认实现由渲染后端
/// （RobotSimulation.OpenGL 的 EmbeddedShaders）提供，宿主无需自行管理 shader 资源。
/// </summary>
public enum RenderPassKind
{
    /// <summary>不透明模型（三角形列表，PBR / Blinn-Phong 光照）。</summary>
    Model = 0,

    /// <summary>无光照线条（网格地面、坐标轴、路径预览等）。</summary>
    Line = 1,

    /// <summary>无光照点集（点云等）。</summary>
    Point = 2,

    /// <summary>环境天空盒（场景级背景，非普通节点绘制）。</summary>
    Skybox = 3,

    /// <summary>
    /// 坐标轴专用通道：纯色箭头，顶点着色器做"恒定屏幕尺寸"补偿——
    /// 仅保留轴的世界朝向/位置，视觉长度不随父级 Scale 放大、也不随相机距离缩小。
    /// </summary>
    Axes = 4,
}
