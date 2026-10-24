using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Pure-CPU collision queries over scene data. This is geometric detection only: it never applies
/// impulses, integrates bodies or resolves contacts. Run <see cref="Detect"/> on the scene's owner
/// thread, like <see cref="SceneGraph.Pick"/>.
/// </summary>
public static class CollisionQuery
{
    /// <summary>
    /// Tests two shapes with explicit world poses. <paramref name="contact"/> is only valid when the
    /// method returns true.
    /// </summary>
    public static bool TryGetContact(CollisionShape a, Matrix4x4 worldA,
        CollisionShape b, Matrix4x4 worldB, out ShapeContact contact)
        => NarrowPhase.TryGetContact(a, worldA, b, worldB, out contact);

    /// <summary>
    /// Finds every overlapping pair of collision shapes in <paramref name="scene"/>'s visible collision
    /// subtrees. <see cref="GameObject.Visible"/> and <see cref="GameObject.Pickable"/> are ignored; a
    /// node opts out through <see cref="GameObject.CollisionEnabled"/> or the predicate.
    /// </summary>
    /// <param name="scene">Scene to query.</param>
    /// <param name="predicate">Optional filter; an object returning false does not participate.</param>
    /// <returns>One contact per overlapping shape pair (possibly several for a single object pair).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public static IReadOnlyList<CollisionContact> Detect(SceneGraph scene,
        Func<GameObject, bool>? predicate = null)
    {
        if (scene is null)
            throw new ArgumentNullException(nameof(scene));

        var entries = new List<Entry>();
        foreach (GameObject root in scene.Roots)
            Collect(root, predicate, entries);

        var contacts = new List<CollisionContact>();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry a = entries[i];
            for (int j = i + 1; j < entries.Count; j++)
            {
                Entry b = entries[j];
                if (ReferenceEquals(a.Owner, b.Owner))
                    continue;
                if (!a.Bounds.Intersects(b.Bounds))
                    continue;
                if (!NarrowPhase.TryGetContact(a.Instance.Shape, a.World, b.Instance.Shape, b.World,
                        out ShapeContact contact))
                    continue;

                contacts.Add(new CollisionContact(a.Owner, b.Owner, contact.Point, contact.Normal, contact.Depth));
            }
        }

        return contacts;
    }

    private static void Collect(GameObject node, Func<GameObject, bool>? predicate, List<Entry> output)
    {
        if (!node.CollisionEnabled)
            return;

        if (predicate is null || predicate(node))
        {
            Matrix4x4 nodeWorld = node.Transform.GetModelMatrix();
            foreach (CollisionShapeInstance instance in node.CollisionShapes)
            {
                Matrix4x4 world = instance.LocalPose * nodeWorld;
                Bounds bounds = instance.Shape.GetLocalBounds().Transformed(world);
                output.Add(new Entry(node, instance, world, bounds));
            }
        }

        foreach (Transform child in node.Transform.Children)
            Collect(child.Owner, predicate, output);
    }

    private readonly record struct Entry(GameObject Owner, CollisionShapeInstance Instance,
        Matrix4x4 World, Bounds Bounds);
}
