using System;
using System.Numerics;

namespace RobotSimulation.Core.Utils;

public static class MathUtils
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float DegToRad = Pi / 180f;
    public const float RadToDeg = 180f / Pi;

    /// <summary>
    /// 角度转弧度
    /// </summary>
    public static float DegreesToRadians(float degrees) => degrees * DegToRad;

    /// <summary>
    /// 弧度转角度
    /// </summary>
    public static float RadiansToDegrees(float radians) => radians * RadToDeg;

    /// <summary>
    /// 钳制数值到指定范围
    /// </summary>
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    /// <summary>
    /// 线性插值
    /// </summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// 将角度归一化到 [-180, 180] 范围
    /// </summary>
    public static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees < -180f) degrees += 360f;
        return degrees;
    }
}