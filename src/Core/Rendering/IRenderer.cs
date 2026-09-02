using System;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Core.Rendering;

/// <summary>
/// 渲染器（对外接口层）：负责把场景绘制到当前渲染上下文。
/// 实现层（如 RobotSimulation.OpenGL.Rendering.Renderer）内部持有 GPU 资源生命周期；
/// 调用方只依赖本接口与纯数据场景（SceneGraph/GameObject/MeshData/MaterialData）。
/// <see cref="Render"/> 必须在渲染线程调用。
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>绘制整个场景。</summary>
    void Render(SceneGraph scene);
}
