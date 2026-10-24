using System.Numerics;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// One overlap found by <see cref="CollisionQuery"/>. <see cref="Normal"/> points from
/// <see cref="A"/> to <see cref="B"/>; <see cref="Depth"/> is non-negative. The contact point and
/// depth are exact for primitive pairs and approximate for mesh / box-box pairs.
/// </summary>
public readonly record struct CollisionContact(
    GameObject A,
    GameObject B,
    Vector3 Point,
    Vector3 Normal,
    float Depth);
