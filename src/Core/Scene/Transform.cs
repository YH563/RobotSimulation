using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RobotSimulation.Robot")]

namespace RobotSimulation.Core.Scene;

public class Transform
{
    public GameObject Owner { get; }
    public List<Transform> Children { get; } = new();

    private Vector3 _position = Vector3.Zero;
    private Quaternion _rotation = Quaternion.Identity;
    private Vector3 _scale = Vector3.One;

    /// <summary>
    /// 本变换是否只读。true 时 <see cref="Position"/> / <see cref="Rotation"/> / <see cref="Scale"/>
    /// 的 setter 会抛出 <see cref="InvalidOperationException"/>；框架/系统内部（如机器人关节驱动、
    /// 位姿装配）应使用 <see cref="SetLocalPose"/> 绕过只读保护。
    /// 用于保证"派生量"（如机器人 link 的局部位姿）只能经宿主对象的官方接口修改，禁止外部直接改写。
    /// </summary>
    public bool IsReadOnly { get; private set; }

    public Vector3 Position
    {
        get => _position;
        set
        {
            GuardReadOnly(nameof(Position));
            _position = value;
        }
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            GuardReadOnly(nameof(Rotation));
            _rotation = value;
        }
    }

    public Vector3 Scale
    {
        get => _scale;
        set
        {
            GuardReadOnly(nameof(Scale));
            _scale = value;
        }
    }

    /// <summary>设置只读标志（框架级 API，仅 <c>RobotSimulation.Robot</c> 程序集内部可用；机器人构建完成后整树锁定）。</summary>
    internal void SetReadOnly(bool value) => IsReadOnly = value;

    /// <summary>
    /// 框架/系统级"直接写入"入口：无视 <see cref="IsReadOnly"/>，一次性设置位置/旋转/缩放。
    /// 仅 <c>RobotSimulation.Robot</c> 程序集内部（<c>RobotModel.SetJointValue</c>/<c>RootPose</c>）可用，
    /// 外部代码无论经 <see cref="Children"/> 还是 <see cref="Owner"/> 都无法调用，子 link 的位姿无法被外部改写。
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
                $"Transform '{Owner?.Name}' 的 {property} 为只读（属于受保护子树，请通过宿主对象的内置接口修改）。");
    }

    private Transform? _parent;

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

    public Transform(GameObject owner)
    {
        Owner = owner;
    }

    public Matrix4x4 GetLocalMatrix()
    {
        return Matrix4x4.CreateScale(Scale) *
               Matrix4x4.CreateFromQuaternion(Rotation) *
               Matrix4x4.CreateTranslation(Position);
    }

    public Matrix4x4 GetModelMatrix()
    {
        var local = GetLocalMatrix();
        if (Parent != null)
            return local * Parent.GetModelMatrix();
        return local;
    }

    /// <summary>
    /// 世界矩阵的逆矩阵（模型矩阵不可逆时返回 <see cref="Matrix4x4.Identity"/>）。
    /// 用于把世界坐标/方向变换回局部（如拾取）。
    /// </summary>
    public Matrix4x4 GetWorldInverseMatrix()
        => Matrix4x4.Invert(GetModelMatrix(), out var inv) ? inv : Matrix4x4.Identity;

    /// <summary>把世界坐标点变换到本节点局部坐标。</summary>
    public Vector3 WorldToLocal(Vector3 worldPoint)
        => Vector3.Transform(worldPoint, GetWorldInverseMatrix());

    /// <summary>把本节点局部坐标点变换到世界坐标。</summary>
    public Vector3 LocalToWorld(Vector3 localPoint)
        => Vector3.Transform(localPoint, GetModelMatrix());
}