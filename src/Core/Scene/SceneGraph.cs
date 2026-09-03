using System;
using System.Collections.Generic;
using RobotSimulation.Core.GameObjects;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 场景图容器：持有场景根对象树、活动相机与一组光源。
/// Camera 与 Light 都是 GameObject 体系内的特殊对象：相机由本容器直接持有引用，
/// 光源通过 <see cref="Add"/> 加入（若实例是 <see cref="Light"/> 会自动进入
/// <see cref="Lights"/> 列表），渲染器据此把多光源参数传入 shader。
/// </summary>
public class SceneGraph : IDisposable
{
    private readonly List<GameObject> _roots = new();
    private readonly List<Light> _lights = new();

    /// <summary>场景根节点（供渲染器遍历与外部只读访问）。</summary>
    public IReadOnlyList<GameObject> Roots => _roots;

    /// <summary>场景活动相机（默认内置一个轨道相机，可整体替换）。</summary>
    public Camera Camera { get; set; } = new Camera();

    /// <summary>场景中的所有光源（由 <see cref="Add"/> 自动收集）。</summary>
    public IReadOnlyList<Light> Lights => _lights;

    private bool _disposed;

    /// <summary>
    /// 添加对象（自动识别是否为根节点）。若 <paramref name="obj"/> 是 <see cref="Light"/>
    /// 且此前未登记，则同时加入 <see cref="Lights"/>。
    /// </summary>
    public void Add(GameObject? obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (obj.Transform.Parent == null)
            _roots.Add(obj);

        if (obj is Light light && !_lights.Contains(light))
            _lights.Add(light);
    }

    /// <summary>
    /// 移除游戏对象，自动从父级或根列表中移除；若是 <see cref="Light"/> 同时从光源列表移除。
    /// </summary>
    public void Remove(GameObject? obj)
    {
        if (obj == null) return;

        if (obj is Light light)
            _lights.Remove(light);

        if (obj.Transform.Parent == null)
        {
            // 如果是根节点，直接从根列表移除
            _roots.Remove(obj);
        }
        else
        {
            // 如果不是根节点，将 Parent 设为 null，自动从父级 Children 移除
            obj.Transform.Parent = null;
        }
    }

    /// <summary>
    /// 递归更新所有节点（由后台线程调用，严禁操作 OpenGL）
    /// </summary>
    public void Update(double deltaTime)
    {
        foreach (var root in _roots)
            UpdateRecursive(root, deltaTime);
    }

    private void UpdateRecursive(GameObject node, double deltaTime)
    {
        node.Update(deltaTime);
        foreach (var child in node.Transform.Children)
            UpdateRecursive(child.Owner, deltaTime);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // GPU 网格/材质由渲染器（Renderer）统一释放；
        // 场景本身只持有数据引用，此处仅清理节点列表。
        _roots.Clear();
        _lights.Clear();
        _disposed = true;
    }
}