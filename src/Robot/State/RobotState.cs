using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Robot.State;

/// <summary>
/// Robot runtime state: maintains "drivable joint values" on top of a <see cref="RobotDescription"/> and
/// computes forward kinematics (FK).
///
/// Design constraints:
///  - Pure computation: no rendering/GL/UI dependency, usable headless (trajectory, physics, and
///    visualization can share the same state source);
///  - Drivable joints = Revolute / Continuous / Prismatic (in RobotDescription.Joints order);
///    Fixed joints only participate in the chain and do not occupy a drive slot;
///  - Joint value units: radians for revolute, meters for prismatic (along the Axis direction);
///  - Poses are row-major matrices (consistent with Core.Scene.Transform);
///    <see cref="GetLinkGlobalPose"/> results can be written directly to a GameObject.Transform or
///    consumed by other systems;
///  - Not thread-safe: assume single-threaded use; the upper layer (a Sink) snapshots it for cross-thread pushes.
/// </summary>
public sealed class RobotState
{
    private readonly RobotDescription _description;

    // Drivable joints (in description order) + value buffer.
    private readonly List<Joint> _drivableJoints = new();
    private readonly Dictionary<string, int> _drivableIndexByName = new(StringComparer.Ordinal);
    private readonly float[] _jointValues;

    // Topology (parents before children).
    private readonly List<string> _topoLinks = new();
    private readonly Dictionary<string, int> _topoIndexOf = new(StringComparer.Ordinal);

    // link → the parent joint that connects it (used by FK to walk up the chain for the parent pose).
    private readonly Dictionary<string, Joint> _incomingJoint = new(StringComparer.Ordinal);

    private Matrix4x4[] _poses = Array.Empty<Matrix4x4>();
    private Matrix4x4 _rootPose = Matrix4x4.Identity;
    private bool _dirty = true;

    /// <summary>
    /// Builds the state for a robot description: collects its drivable joints, links each link to its
    /// parent joint and computes a parents-before-children topological order.
    /// </summary>
    /// <param name="description">The parsed robot description to drive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    public RobotState(RobotDescription description)
    {
        _description = description ?? throw new ArgumentNullException(nameof(description));

        // 1) Collect the drivable joints (in description order).
        foreach (Joint joint in description.Joints)
        {
            if (joint.Type is JointType.Revolute or JointType.Continuous or JointType.Prismatic)
            {
                _drivableIndexByName[joint.Name] = _drivableJoints.Count;
                _drivableJoints.Add(joint);
            }
        }

        _jointValues = new float[_drivableJoints.Count];

        // 2) Each link has at most one parent joint (tree constraint); cycles/broken chains surface in the
        //    topological sort phase.
        foreach (Joint joint in description.Joints)
        {
            if (!_incomingJoint.TryAdd(joint.ChildLinkName, joint))
                throw new ArgumentException(
                    $"link '{joint.ChildLinkName}' is the child of multiple joints — RobotDescription must be a tree.",
                    nameof(description));
        }

        // 3) Topological sort: DFS from the roots, guaranteeing parents precede children.
        var children = new Dictionary<string, List<Joint>>(StringComparer.Ordinal);
        foreach (Joint joint in description.Joints)
        {
            if (!children.TryGetValue(joint.ParentLinkName, out List<Joint>? list))
            {
                list = new List<Joint>();
                children[joint.ParentLinkName] = list;
            }

            list.Add(joint);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (Link root in description.RootLinks)
            Visit(children, root.Name, visited);

        if (_topoLinks.Count != description.Links.Count)
            throw new ArgumentException(
                "RobotDescription contains unreachable links (cycle or broken chain); cannot build a kinematic chain.",
                nameof(description));

        for (int i = 0; i < _topoLinks.Count; i++)
            _topoIndexOf[_topoLinks[i]] = i;

        _poses = new Matrix4x4[_topoLinks.Count];
    }

    private void Visit(Dictionary<string, List<Joint>> children, string linkName, HashSet<string> visited)
    {
        if (!visited.Add(linkName))
            throw new ArgumentException($"Joint chain has a cycle: link '{linkName}' was revisited.", nameof(_description));

        _topoLinks.Add(linkName);
        if (children.TryGetValue(linkName, out List<Joint>? childJoints))
        {
            foreach (Joint child in childJoints)
                Visit(children, child.ChildLinkName, visited);
        }
    }

    // ------------------------------------------------------------------
    // Public interface
    // ------------------------------------------------------------------

    /// <summary>The static description used at construction.</summary>
    public RobotDescription Description => _description;

    /// <summary>The list of drivable joints (Revolute/Continuous/Prismatic, in description order).</summary>
    public IReadOnlyList<Joint> DrivableJoints => _drivableJoints;

    /// <summary>Number of drivable joints (i.e. the array length required by <see cref="ApplyJointValues"/>).</summary>
    public int DrivableJointCount => _drivableJoints.Count;

    /// <summary>
    /// The robot root's pose in world (Identity by default). All FK results are based on this.
    /// </summary>
    public Matrix4x4 RootPose
    {
        get => _rootPose;
        set
        {
            _rootPose = value;
            _dirty = true;
        }
    }

    /// <summary>
    /// Sets a joint value by name (Revolute/Continuous in radians, Prismatic in meters along the axis).
    /// </summary>
    public void SetJointValue(string name, float value)
    {
        _jointValues[ResolveDrivable(name)] = value;
        _dirty = true;
    }

    /// <summary>Reads the current value of a drivable joint.</summary>
    public float GetJointValue(string name)
    {
        return _jointValues[ResolveDrivable(name)];
    }

    /// <summary>
    /// Sets joint values in bulk, in drivable-joint order.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the length does not equal <see cref="DrivableJointCount"/>.</exception>
    public void ApplyJointValues(IReadOnlyList<float> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));
        if (values.Count != _drivableJoints.Count)
            throw new ArgumentException(
                $"Joint value count ({values.Count}) does not match the drivable joint count ({_drivableJoints.Count}).",
                nameof(values));

        for (int i = 0; i < values.Count; i++)
            _jointValues[i] = values[i];
        _dirty = true;
    }

    /// <summary>Computes a link's global pose relative to <see cref="RootPose"/> (updates FK first, then queries).</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the link name does not exist.</exception>
    public Matrix4x4 GetLinkGlobalPose(string linkName)
    {
        RecomputeIfDirty();
        return _poses[_topoIndexOf[linkName]];
    }

    /// <summary>Try version of <see cref="GetLinkGlobalPose"/>.</summary>
    public bool TryGetLinkGlobalPose(string linkName, out Matrix4x4 pose)
    {
        RecomputeIfDirty();
        if (_topoIndexOf.TryGetValue(linkName, out int index))
        {
            pose = _poses[index];
            return true;
        }

        pose = Matrix4x4.Identity;
        return false;
    }

    // ------------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------------

    private int ResolveDrivable(string name)
    {
        if (!_drivableIndexByName.TryGetValue(name, out int index))
            throw new KeyNotFoundException(
                $"Joint '{name}' does not exist or is not drivable (Fixed joints do not occupy a drive slot).");
        return index;
    }

    private void RecomputeIfDirty()
    {
        if (!_dirty)
            return;

        for (int i = 0; i < _topoLinks.Count; i++)
        {
            string linkName = _topoLinks[i];
            if (!_incomingJoint.TryGetValue(linkName, out Joint? joint))
            {
                // Root: its pose is RootPose.
                _poses[i] = _rootPose;
                continue;
            }

            int parentIndex = _topoIndexOf[joint.ParentLinkName];
            _poses[i] = _poses[parentIndex] * joint.Origin * JointMotion(joint, ValueOf(joint));
        }

        _dirty = false;
    }

    private float ValueOf(Joint joint)
    {
        return _drivableIndexByName.TryGetValue(joint.Name, out int index)
            ? _jointValues[index]
            : 0f; // Non-drivable joints (e.g. Fixed).
    }

    private static Matrix4x4 JointMotion(Joint joint, float q)
    {
        switch (joint.Type)
        {
            case JointType.Revolute:
            case JointType.Continuous:
                // Rotate about the Axis (radians).
                return Matrix4x4.CreateFromQuaternion(
                    Quaternion.CreateFromAxisAngle(joint.Axis, q));

            case JointType.Prismatic:
                // Translate along the Axis (meters).
                return Matrix4x4.CreateTranslation(joint.Axis * q);

            default:
                return Matrix4x4.Identity; // Fixed.
        }
    }
}
