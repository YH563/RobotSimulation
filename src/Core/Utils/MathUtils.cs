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
    /// Converts degrees to radians.
    /// </summary>
    public static float DegreesToRadians(float degrees) => degrees * DegToRad;

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    public static float RadiansToDegrees(float radians) => radians * RadToDeg;

    /// <summary>
    /// Clamps a value to the given range.
    /// </summary>
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Linear interpolation.
    /// </summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Normalizes an angle to the [-180, 180] range.
    /// </summary>
    public static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees < -180f) degrees += 360f;
        return degrees;
    }
}
