using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Scene;

namespace RobotSimulation.Robot.Collision;

/// <summary>
/// Translucent collision-shape view for a <see cref="RobotCollisionModel"/>. One fill node is mounted
/// under each link, so the view follows joint motion without per-frame updates. The nodes use the
/// debug render pass and are not pickable.
/// </summary>
public sealed class RobotCollisionDebugView : IDisposable
{
    private readonly List<GameObject> _nodes = new();
    private bool _visible = true;
    private bool _disposed;

    internal RobotCollisionDebugView(RobotCollisionModel model, Vector4? color, Vector4? outlineColor)
    {
        foreach (GameObject link in model.LinkNodes.Values)
        {
            foreach (CollisionShapeInstance instance in link.CollisionShapes)
            {
                GameObject node = CollisionDebug.CreateLocalFill(instance, color,
                    $"{link.Name}:collision-debug", outlineColor);
                node.Transform.Parent = link.Transform;
                _nodes.Add(node);
            }
        }
    }

    /// <summary>Number of collision fill nodes mounted by this view.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>Whether the collision fills are visible.</summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            foreach (GameObject node in _nodes)
                node.Visible = value;
        }
    }

    /// <summary>Detaches every debug node from its link (idempotent).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (GameObject node in _nodes)
            node.Transform.Parent = null;
        _nodes.Clear();
    }
}
