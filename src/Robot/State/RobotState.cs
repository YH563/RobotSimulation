using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Robot.Description;

namespace RobotSimulation.Robot.State;

/// <summary>
/// 机器人运行时状态：在 <see cref="RobotDescription"/> 之上维护"可驱动关节值"并计算正向运动学（FK）。
///
/// 设计约束：
///  - 纯计算：不依赖渲染/GL/UI，可 headless 使用（轨迹、物理、可视化可共用同一状态源）；
///  - 可驱动关节 = Revolute / Continuous / Prismatic（按 RobotDescription.Joints 顺序）；
///    Fixed 关节只参与链路、不占驱动位；
///  - 关节值单位：旋转类用弧度、平移类用米（沿 Axis 方向）；
///  - 位姿为行主序矩阵（与 Core.Scene.Transform 约定一致）；
///    <see cref="GetLinkGlobalPose"/> 的结果可直接写入 GameObject.Transform 或供其它系统消费；
///  - 非线程安全：假定单线程使用；需要跨线程推送时由上层（Sink）做快照。
/// </summary>
public sealed class RobotState
{
    private readonly RobotDescription _description;

    // 可驱动关节（按描述中 Joints 的顺序）+ 值缓冲
    private readonly List<Joint> _drivableJoints = new();
    private readonly Dictionary<string, int> _drivableIndexByName = new(StringComparer.Ordinal);
    private readonly float[] _jointValues;

    // 拓扑（父先于子）
    private readonly List<string> _topoLinks = new();
    private readonly Dictionary<string, int> _topoIndexOf = new(StringComparer.Ordinal);

    // link → 连接它的父关节（用于 FK 沿链向上取 parent pose）
    private readonly Dictionary<string, Joint> _incomingJoint = new(StringComparer.Ordinal);

    private Matrix4x4[] _poses = Array.Empty<Matrix4x4>();
    private Matrix4x4 _rootPose = Matrix4x4.Identity;
    private bool _dirty = true;

    public RobotState(RobotDescription description)
    {
        _description = description ?? throw new ArgumentNullException(nameof(description));

        // 1) 收集可驱动关节（按描述顺序）
        foreach (Joint joint in description.Joints)
        {
            if (joint.Type is JointType.Revolute or JointType.Continuous or JointType.Prismatic)
            {
                _drivableIndexByName[joint.Name] = _drivableJoints.Count;
                _drivableJoints.Add(joint);
            }
        }

        _jointValues = new float[_drivableJoints.Count];

        // 2) 每个 link 至多一个父关节（树约束）；成环/断链由拓扑排序阶段暴露
        foreach (Joint joint in description.Joints)
        {
            if (!_incomingJoint.TryAdd(joint.ChildLinkName, joint))
                throw new ArgumentException(
                    $"link '{joint.ChildLinkName}' 被多个关节作为 child link——RobotDescription 必须是树。",
                    nameof(description));
        }

        // 3) 拓扑排序：从根开始 DFS，保证父先于子
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
                "RobotDescription 中存在不可达的 link（成环或断链），无法建立运动学链。",
                nameof(description));

        for (int i = 0; i < _topoLinks.Count; i++)
            _topoIndexOf[_topoLinks[i]] = i;

        _poses = new Matrix4x4[_topoLinks.Count];
    }

    private void Visit(Dictionary<string, List<Joint>> children, string linkName, HashSet<string> visited)
    {
        if (!visited.Add(linkName))
            throw new ArgumentException($"关节链成环：link '{linkName}' 被重复访问。", nameof(_description));

        _topoLinks.Add(linkName);
        if (children.TryGetValue(linkName, out List<Joint>? childJoints))
        {
            foreach (Joint child in childJoints)
                Visit(children, child.ChildLinkName, visited);
        }
    }

    // ------------------------------------------------------------------
    // 对外接口
    // ------------------------------------------------------------------

    /// <summary>构造时使用的静态描述。</summary>
    public RobotDescription Description => _description;

    /// <summary>可被驱动的关节列表（Revolute/Continuous/Prismatic，按描述顺序）。</summary>
    public IReadOnlyList<Joint> DrivableJoints => _drivableJoints;

    /// <summary>可驱动关节数量（即 <see cref="ApplyJointValues"/> 所需数组长度）。</summary>
    public int DrivableJointCount => _drivableJoints.Count;

    /// <summary>
    /// 机器人根部在世界中的位姿（默认 Identity）。所有 FK 结果以此为基准。
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
    /// 按名称设置关节值（Revolute/Continuous 为弧度，Prismatic 为沿轴的米）。
    /// </summary>
    public void SetJointValue(string name, float value)
    {
        _jointValues[ResolveDrivable(name)] = value;
        _dirty = true;
    }

    /// <summary>读取某可驱动关节当前值。</summary>
    public float GetJointValue(string name)
    {
        return _jointValues[ResolveDrivable(name)];
    }

    /// <summary>
    /// 按可驱动关节顺序批量设置关节值。
    /// </summary>
    /// <exception cref="ArgumentException">长度不等于 <see cref="DrivableJointCount"/> 时抛出。</exception>
    public void ApplyJointValues(IReadOnlyList<float> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));
        if (values.Count != _drivableJoints.Count)
            throw new ArgumentException(
                $"关节值数量({values.Count})与可驱动关节数({_drivableJoints.Count})不一致。",
                nameof(values));

        for (int i = 0; i < values.Count; i++)
            _jointValues[i] = values[i];
        _dirty = true;
    }

    /// <summary>计算某 link 相对 <see cref="RootPose"/> 的全局位姿（先更新 FK，再查询）。</summary>
    /// <exception cref="KeyNotFoundException">link 名称不存在时抛出。</exception>
    public Matrix4x4 GetLinkGlobalPose(string linkName)
    {
        RecomputeIfDirty();
        return _poses[_topoIndexOf[linkName]];
    }

    /// <summary>Try 版 <see cref="GetLinkGlobalPose"/>。</summary>
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
    // 内部
    // ------------------------------------------------------------------

    private int ResolveDrivable(string name)
    {
        if (!_drivableIndexByName.TryGetValue(name, out int index))
            throw new KeyNotFoundException(
                $"关节 '{name}' 不存在或不可驱动（Fixed 关节不占驱动位）。");
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
                // 根：位姿即 RootPose
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
            : 0f; // Fixed 等非驱动关节
    }

    private static Matrix4x4 JointMotion(Joint joint, float q)
    {
        switch (joint.Type)
        {
            case JointType.Revolute:
            case JointType.Continuous:
                // 绕 Axis 旋转（弧度）
                return Matrix4x4.CreateFromQuaternion(
                    Quaternion.CreateFromAxisAngle(joint.Axis, q));

            case JointType.Prismatic:
                // 沿 Axis 平移（米）
                return Matrix4x4.CreateTranslation(joint.Axis * q);

            default:
                return Matrix4x4.Identity; // Fixed
        }
    }
}