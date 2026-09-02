using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.GameObjects;
using RobotSimulation.Core.Scene;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Robot;

/// <summary>
/// 一个机器人实例在场景中的表现形式。
/// 它本身就是一个 <see cref="GameObject"/>（机器人的根 link 骨架节点），因此与场景中
/// 任何其它对象完全平等：可 <see cref="SceneGraph.Add"/>、可挂到任意父 Transform 下、
/// 参与可见性/释放等统一管理。额外提供关节驱动接口。
///
/// 子结构：每个 link 是一个普通 GameObject 骨架，visual 是其子节点；关节把子树
/// 逐级挂到父 link 之下。由 <see cref="RobotModel.CreateGameObject"/> 创建。
/// </summary>
public sealed class RobotGameObject : GameObject
{
    // 可驱动关节绑定（按 RobotDescription.Joints 中出现的顺序）
    private readonly List<Joint> _drivableJoints = new();
    private readonly Dictionary<string, int> _drivableIndexByName = new(StringComparer.Ordinal);
    private readonly List<GameObject> _drivableChildLinks = new();
    private float[] _jointValues = Array.Empty<float>();

    /// <summary>机器人名称（URDF &lt;robot name&gt;）。</summary>
    public string RobotName { get; }

    internal RobotGameObject(string rootLinkName, string? robotName)
        : base(null, null, rootLinkName)
    {
        RobotName = robotName ?? string.Empty;
    }

    /// <summary>装配完成后由 <see cref="RobotModel.CreateGameObject"/> 注册可驱动关节绑定。</summary>
    internal void ConfigureDriving(RobotDescription robot, IReadOnlyDictionary<string, GameObject> linkNodes)
    {
        foreach (Joint joint in robot.Joints)
        {
            if (joint.Type is not (JointType.Revolute or JointType.Continuous or JointType.Prismatic))
                continue;

            _drivableIndexByName[joint.Name] = _drivableJoints.Count;
            _drivableJoints.Add(joint);
            _drivableChildLinks.Add(linkNodes[joint.ChildLinkName]);
        }
        _jointValues = new float[_drivableJoints.Count];
    }

    /// <summary>可驱动关节列表（Revolute/Continuous/Prismatic，按描述顺序）。</summary>
    public IReadOnlyList<Joint> DrivableJoints => _drivableJoints;

    /// <summary>可驱动关节数量（即 <see cref="ApplyJointValues"/> 所需数组长度）。</summary>
    public int DrivableJointCount => _drivableJoints.Count;

    /// <summary>
    /// 按名称设置关节值（旋转弧度 / 平移沿轴的米），并立即更新对应子 link 的局部 Transform。
    /// 仅操作 Transform（纯数据），不触碰 OpenGL。
    /// </summary>
    public void SetJointValue(string name, float value)
    {
        int index = ResolveDrivable(name);
        _jointValues[index] = value;
        ApplyJointNode(index);
    }

    /// <summary>按可驱动关节顺序批量设置关节值并一次更新整树。</summary>
    public void ApplyJointValues(IReadOnlyList<float> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));
        if (values.Count != _drivableJoints.Count)
            throw new ArgumentException(
                $"关节值数量({values.Count})与可驱动关节数({_drivableJoints.Count})不一致。",
                nameof(values));

        for (int i = 0; i < values.Count; i++)
        {
            _jointValues[i] = values[i];
            ApplyJointNode(i);
        }
    }

    private int ResolveDrivable(string name)
    {
        if (!_drivableIndexByName.TryGetValue(name, out int index))
            throw new KeyNotFoundException(
                $"关节 '{name}' 不存在或不可驱动（Fixed 关节不占驱动位）。");
        return index;
    }

    private void ApplyJointNode(int index)
    {
        Joint joint = _drivableJoints[index];
        GameObject childLink = _drivableChildLinks[index];

        // 子 link 局部位姿 = joint.origin * 关节运动(q)；行主序与 GetModelMatrix 级联一致
        Matrix4x4 local = joint.Origin * JointMotion(joint, _jointValues[index]);
        if (Matrix4x4.Decompose(local, out _, out Quaternion rotation, out Vector3 position))
        {
            childLink.Transform.Position = position;
            childLink.Transform.Rotation = rotation;
        }
    }

    private static Matrix4x4 JointMotion(Joint joint, float q)
    {
        switch (joint.Type)
        {
            case JointType.Revolute:
            case JointType.Continuous:
                return Matrix4x4.CreateFromQuaternion(
                    Quaternion.CreateFromAxisAngle(joint.Axis, q));
            case JointType.Prismatic:
                return Matrix4x4.CreateTranslation(joint.Axis * q);
            default:
                return Matrix4x4.Identity;
        }
    }
}
