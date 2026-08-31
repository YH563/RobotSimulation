using System;
using System.Collections.Generic;
using RobotSimulation.Core.Rendering;
using System.Numerics;

namespace RobotSimulation.Core.Scene;

public class Scene : IDisposable
{
    private readonly List<GameObject> _roots = new();
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

    /// <summary>
    /// 递归渲染所有可见节点（由主线程调用）
    /// </summary>
    public void Render()
    {
        foreach (var root in _roots)
            RenderRecursive(root);
    }

    private void RenderRecursive(GameObject node)
    {
        if (!node.Visible || node.Mesh == null || node.Material == null)
            return;

        node.Material.Apply();
        var shader = node.Material.Shader;
        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", Camera.GetViewMatrix());
        shader.SetUniform("uProjection", Camera.GetProjectionMatrix());
        shader.SetUniform("uLightPos", LightPosition);
        shader.SetUniform("uLightColor", LightColor);
        shader.SetUniform("uViewPos", Camera.Position);

        node.Mesh.Draw();

        foreach (var child in node.Transform.Children)
            RenderRecursive(child.Owner);
    }
    
    public void Dispose()
    {
        if (_disposed) return;

        void DisposeRecursive(GameObject node)
        {
            node.Mesh?.Dispose();
            node.Material?.Dispose();
            foreach (var child in node.Transform.Children)
                DisposeRecursive(child.Owner);
        }

        foreach (var root in _roots)
            DisposeRecursive(root);

        _roots.Clear();
        _disposed = true;
    }
}