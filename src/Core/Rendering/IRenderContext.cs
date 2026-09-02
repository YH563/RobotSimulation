using System;
using System.Numerics;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 渲染上下文（对外接口层）：视口管理、清屏等与具体图形 API 无关的设备能力。
/// 实现层（如 RobotSimulation.OpenGL.GraphicsContext）包装具体 GL 上下文；
/// 调用方只依赖本接口，不接触任何 Silk/GL 类型。
/// 颜色统一为 RGBA、分量范围 [0,1]（Vector4），不引入 System.Drawing 等平台类型。
/// </summary>
public interface IRenderContext : IDisposable
{
    /// <summary>当视口大小变化时触发，参数为新的宽和高。</summary>
    event Action<int, int>? Resized;

    /// <summary>更新视口尺寸（通常由宿主在窗口 Resize 时调用，并触发 <see cref="Resized"/>）。</summary>
    void Resize(int width, int height);

    /// <summary>
    /// 清空颜色缓冲（及可选的深度缓冲）。
    /// </summary>
    /// <param name="clearColor">清屏颜色 RGBA，分量范围 [0,1]。</param>
    /// <param name="clearDepth">是否同时清空深度缓冲，默认 true。</param>
    void Clear(Vector4 clearColor, bool clearDepth = true);
}
