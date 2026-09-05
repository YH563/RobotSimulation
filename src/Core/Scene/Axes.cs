using System;
using System.Numerics;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// 坐标轴（<see cref="GameObject"/>，模型通道）：原点处三支 RGB 实心箭头——
/// +X 红、+Y 绿、+Z 蓝（rviz 风格、比细线更醒目）。整体可随 <see cref="Transform"/> 放置/旋转。
/// </summary>
public sealed class Axes : GameObject
{
    private static readonly Quaternion ToX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
    private static readonly Quaternion ToY = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);

    /// <summary>箭头长度（各轴从原点伸出的长度）。</summary>
    public float Length { get; }

    // ---- 恒定屏幕尺寸参数（坐标轴内部管理，Renderer 只读取）----

    /// <summary>恒定屏幕尺寸系数：目标世界长 = <see cref="ScreenScale"/> × 相机到轴原点距离。</summary>
    public const float DefaultScreenScale = 0.08f;

    /// <summary>轴世界长度下限。</summary>
    public const float DefaultMinLength = 0.15f;

    /// <summary>轴世界长度上限。</summary>
    public const float DefaultMaxLength = 1.5f;

    /// <summary>恒定屏幕尺寸系数（想调整各对象坐标轴大小/手感改这里，Renderer 不持有该常量）。</summary>
    public float ScreenScale { get; set; } = DefaultScreenScale;

    /// <summary>轴世界长度下限（相机贴近时不再更小）。</summary>
    public float MinWorldLength { get; set; } = DefaultMinLength;

    /// <summary>轴世界长度上限（相机很远时不再更大）。</summary>
    public float MaxWorldLength { get; set; } = DefaultMaxLength;

    public Axes(
        float length = 1.2f,
        float shaftRadius = 0.01f,
        float headRadius = 0.03f,
        float headLength = 0.1f,
        string? name = null)
        : base(null, null, name ?? nameof(Axes))
    {
        if (length <= 0f || float.IsNaN(length) || float.IsInfinity(length))
            throw new ArgumentOutOfRangeException(nameof(length), length, "轴长必须为正的有限数值。");
        Length = length;

        var x = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.90f, 0.20f, 0.20f, 1f), 24, "axes_x")
        {
            Transform = { Rotation = ToX },
        };
        var y = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.25f, 0.85f, 0.30f, 1f), 24, "axes_y")
        {
            Transform = { Rotation = ToY },
        };
        var z = new Arrow(length, shaftRadius, headRadius, headLength,
            new Vector4(0.25f, 0.45f, 1f, 1f), 24, "axes_z");

        // 坐标轴箭头走专用"恒定尺寸"通道（着色器做屏幕补偿），不是普通模型光照通道
        x.MaterialData!.PassKind = RenderPassKind.Axes;
        y.MaterialData!.PassKind = RenderPassKind.Axes;
        z.MaterialData!.PassKind = RenderPassKind.Axes;

        x.Transform.Parent = Transform;
        y.Transform.Parent = Transform;
        z.Transform.Parent = Transform;
    }
}

