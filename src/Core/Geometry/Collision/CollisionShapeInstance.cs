using System.Numerics;

namespace RobotSimulation.Core.Geometry.Collision;

/// <summary>
/// A collision shape placed relative to its owner. The world pose is
/// <c>LocalPose * owner.Transform.GetModelMatrix()</c> (row-vector convention).
/// </summary>
public readonly record struct CollisionShapeInstance(CollisionShape Shape, Matrix4x4 LocalPose);
