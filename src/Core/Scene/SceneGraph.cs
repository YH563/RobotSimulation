using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.GameObjects;

namespace RobotSimulation.Core.Scene;

public class SceneGraph : IDisposable
{
    private readonly List<GameObject> _roots = new();

    /// <summary>场景根节点（供渲染器遍历与外部只读访问）。</summary>
    public IReadOnlyList<GameObject> Roots => _roots;

    public Camera Camera { get; private set; } = new Camera();
    private bool _disposed = false;
    
    // 光照信息
    public Vector3 LightPosition { get; set; } = new Vector3(5, 10, 5);
    public Vector3 LightColor { get; set; } = Vector3.One;

    /// <summary>
    /// 添加对象（自动识别是否为根节点）
    /// </summary>
    public void Add(GameObject? obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (obj.Transform.Parent == null)
            _roots.Add(obj);
    }

    /// <summary>
    /// 移除游戏对象，自动从父级或根列表中移除。
    /// </summary>
    public void Remove(GameObject? obj)
    {
        if (obj == null) return;

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
        _disposed = true;
    }
}