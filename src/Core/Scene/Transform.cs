using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RobotSimulation.Robot")]

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Local pose of a <see cref="GameObject"/> (position / rotation / scale) plus its place in the scene tree.
/// The pose is local to <see cref="Parent"/>; <see cref="GetModelMatrix"/> composes the chain up to the
/// root to yield the world matrix. A subtree can be locked via <see cref="IsReadOnly"/>.
/// </summary>
public class Transform
{
    /// <summary>Scene node this transform belongs to (set once, at construction).</summary>
    public GameObject Owner { get; }

    /// <summary>Child transforms, in attach order (kept in sync automatically by <see cref="Parent"/>).</summary>
    public List<Transform> Children { get; } = new();

    private Vector3 _position = Vector3.Zero;
    private Quaternion _rotation = Quaternion.Identity;
    private Vector3 _scale = Vector3.One;

    /// <summary>
    /// Whether this transform is read-only. When true, the setters of <see cref="Position"/> /
    /// <see cref="Rotation"/> / <see cref="Scale"/> throw <see cref="InvalidOperationException"/>;
    /// framework/system internals (such as robot joint driving and pose assembly) should use
    /// <see cref="SetLocalPose"/> to bypass the read-only protection. This guarantees that "derived"
    /// quantities (such as a robot link's local pose) can only be changed through the host object's
    /// official interface, forbidding external direct rewrites.
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>Local position relative to the parent (world position when there is no parent).</summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            GuardReadOnly(nameof(Position));
            _position = value;
        }
    }

    /// <summary>Local rotation relative to the parent, as a quaternion.</summary>
    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            GuardReadOnly(nameof(Rotation));
            _rotation = value;
        }
    }

    /// <summary>Local per-axis scale (default <see cref="Vector3.One"/>).</summary>
    public Vector3 Scale
    {
        get => _scale;
        set
        {
            GuardReadOnly(nameof(Scale));
            _scale = value;
        }
    }

    /// <summary>Sets the read-only flag (framework-level API, internal to <c>RobotSimulation.Robot</c>; the whole tree is locked after robot construction).</summary>
    internal void SetReadOnly(bool value) => IsReadOnly = value;

    /// <summary>
    /// Framework/system-level "direct write" entry: ignores <see cref="IsReadOnly"/> and sets
    /// position/rotation/scale at once. Only available internally to <c>RobotSimulation.Robot</c>
    /// (<c>RobotModel.SetJointValue</c>/<c>RootPose</c>); external code cannot call it via
    /// <see cref="Children"/> or <see cref="Owner"/>, so child-link poses cannot be rewritten externally.
    /// </summary>
    internal void SetLocalPose(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _position = position;
        _rotation = rotation;
        _scale = scale;
    }

    private void GuardReadOnly(string property)
    {
        if (IsReadOnly)
            throw new InvalidOperationException(
                $"Transform '{Owner?.Name}' property '{property}' is read-only (it belongs to a protected subtree; modify it through the host object's built-in interface).");
    }

    private Transform? _parent;

    /// <summary>
    /// Parent transform, or null for a scene root. Assigning re-links the child lists on both sides
    /// (the previous parent drops this transform, the new one gains it).
    /// </summary>
    public Transform? Parent
    {
        get => _parent;
        set
        {
            _parent?.Children.Remove(this);
            _parent = value;
            _parent?.Children.Add(this);
        }
    }

    /// <summary>Creates the transform of <paramref name="owner"/> (identity pose, no parent).</summary>
    /// <param name="owner">The scene node this transform belongs to.</param>
    public Transform(GameObject owner)
    {
        Owner = owner;
    }

    /// <summary>Local matrix (scale × rotation × translation), i.e. the pose relative to the parent.</summary>
    public Matrix4x4 GetLocalMatrix()
    {
        return Matrix4x4.CreateScale(Scale) *
               Matrix4x4.CreateFromQuaternion(Rotation) *
               Matrix4x4.CreateTranslation(Position);
    }

    /// <summary>World matrix: this node's local matrix composed with every ancestor up to the root.</summary>
    public Matrix4x4 GetModelMatrix()
    {
        var local = GetLocalMatrix();
        if (Parent != null)
            return local * Parent.GetModelMatrix();
        return local;
    }

    /// <summary>
    /// Inverse of the world matrix (returns <see cref="Matrix4x4.Identity"/> when the model matrix is not invertible).
    /// Used to transform world coordinates/directions back to local (e.g. picking).
    /// </summary>
    public Matrix4x4 GetWorldInverseMatrix()
        => Matrix4x4.Invert(GetModelMatrix(), out var inv) ? inv : Matrix4x4.Identity;

    /// <summary>Transforms a world-space point into this node's local space.</summary>
    public Vector3 WorldToLocal(Vector3 worldPoint)
        => Vector3.Transform(worldPoint, GetWorldInverseMatrix());

    /// <summary>Transforms a local-space point into world space.</summary>
    public Vector3 LocalToWorld(Vector3 localPoint)
        => Vector3.Transform(localPoint, GetModelMatrix());
}
