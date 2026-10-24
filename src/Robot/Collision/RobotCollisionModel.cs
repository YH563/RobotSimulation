using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Scene;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Robot.Collision;

/// <summary>
/// Collision model of one robot tree: link nodes (with their URDF collision shapes) plus the link
/// adjacency derived from the joint list. Queries use the current world poses of those nodes, so they
/// follow joint driving without a second forward-kinematics path.
/// </summary>
public sealed class RobotCollisionModel
{
    private readonly IReadOnlyDictionary<string, GameObject> _linkNodes;
    private readonly List<string> _linkOrder;
    private readonly Dictionary<string, HashSet<string>> _adjacent = new(StringComparer.Ordinal);

    internal RobotCollisionModel(RobotDescription description,
        IReadOnlyDictionary<string, GameObject> linkNodes)
    {
        _linkNodes = linkNodes;
        _linkOrder = new List<string>(description.Links.Count);
        foreach (Link link in description.Links)
            _linkOrder.Add(link.Name);

        foreach (Joint joint in description.Joints)
        {
            if (!_adjacent.TryGetValue(joint.ParentLinkName, out HashSet<string>? parents))
            {
                parents = new HashSet<string>(StringComparer.Ordinal);
                _adjacent[joint.ParentLinkName] = parents;
            }

            parents.Add(joint.ChildLinkName);

            if (!_adjacent.TryGetValue(joint.ChildLinkName, out HashSet<string>? children))
            {
                children = new HashSet<string>(StringComparer.Ordinal);
                _adjacent[joint.ChildLinkName] = children;
            }

            children.Add(joint.ParentLinkName);
        }
    }

    internal IReadOnlyDictionary<string, GameObject> LinkNodes => _linkNodes;

    /// <summary>
    /// Creates a translucent collision-fill view mounted under the link nodes. The view follows joint
    /// motion automatically and can be shown/hidden or disposed by the host.
    /// </summary>
    /// <param name="color">Fill color; null uses <see cref="CollisionDebug.DefaultColor"/>.</param>
    /// <param name="outlineColor">Outline color; null uses <see cref="CollisionDebug.DefaultOutlineColor"/>.</param>
    public RobotCollisionDebugView CreateDebugView(Vector4? color = null, Vector4? outlineColor = null)
        => new(this, color, outlineColor);

    /// <summary>
    /// Detects overlaps between collision shapes on different links. Shapes on the same link are always
    /// skipped. Links directly connected by a joint are skipped unless <paramref name="includeAdjacent"/>
    /// is true. The query is pure CPU geometry: it never resolves contacts or changes poses.
    /// </summary>
    /// <param name="includeAdjacent">When true, also test directly connected links.</param>
    /// <param name="allowPair">Optional filter; return false to skip a link pair.</param>
    /// <returns>One contact per overlapping shape pair, with <see cref="GameObject.Name"/> as the link name.</returns>
    public IReadOnlyList<CollisionContact> DetectSelfCollision(bool includeAdjacent = false,
        Func<GameObject, GameObject, bool>? allowPair = null)
    {
        var contacts = new List<CollisionContact>();
        var worlds = new Dictionary<GameObject, Matrix4x4>(_linkOrder.Count);

        for (int i = 0; i < _linkOrder.Count; i++)
        {
            string nameA = _linkOrder[i];
            GameObject linkA = _linkNodes[nameA];
            if (linkA.CollisionShapes.Count == 0 || !IsCollisionEnabled(linkA))
                continue;

            for (int j = i + 1; j < _linkOrder.Count; j++)
            {
                string nameB = _linkOrder[j];
                GameObject linkB = _linkNodes[nameB];
                if (linkB.CollisionShapes.Count == 0 || !IsCollisionEnabled(linkB))
                    continue;
                if (!includeAdjacent && AreAdjacent(nameA, nameB))
                    continue;
                if (allowPair is not null && !allowPair(linkA, linkB))
                    continue;

                Matrix4x4 worldA = GetWorld(linkA, worlds);
                Matrix4x4 worldB = GetWorld(linkB, worlds);

                foreach (CollisionShapeInstance instanceA in linkA.CollisionShapes)
                {
                    Matrix4x4 shapeWorldA = instanceA.LocalPose * worldA;
                    Bounds boundsA = instanceA.Shape.GetLocalBounds().Transformed(shapeWorldA);

                    foreach (CollisionShapeInstance instanceB in linkB.CollisionShapes)
                    {
                        Matrix4x4 shapeWorldB = instanceB.LocalPose * worldB;
                        Bounds boundsB = instanceB.Shape.GetLocalBounds().Transformed(shapeWorldB);
                        if (!boundsA.Intersects(boundsB))
                            continue;

                        if (CollisionQuery.TryGetContact(instanceA.Shape, shapeWorldA,
                                instanceB.Shape, shapeWorldB, out ShapeContact contact))
                        {
                            contacts.Add(new CollisionContact(linkA, linkB, contact.Point,
                                contact.Normal, contact.Depth));
                        }
                    }
                }
            }
        }

        return contacts;
    }

    private bool AreAdjacent(string a, string b)
        => _adjacent.TryGetValue(a, out HashSet<string>? neighbours) && neighbours.Contains(b);

    private static bool IsCollisionEnabled(GameObject node)
    {
        for (Transform? transform = node.Transform; transform is not null; transform = transform.Parent)
        {
            if (!transform.Owner.CollisionEnabled)
                return false;
        }

        return true;
    }

    private static Matrix4x4 GetWorld(GameObject node, Dictionary<GameObject, Matrix4x4> cache)
    {
        if (cache.TryGetValue(node, out Matrix4x4 world))
            return world;

        world = node.Transform.GetModelMatrix();
        cache[node] = world;
        return world;
    }
}
