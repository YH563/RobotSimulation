using System.Numerics;

namespace RobotSimulation.Core.Geometry.Collision;

/// <summary>
/// Result of a narrow-phase query between two shapes. <see cref="Normal"/> points from the first
/// shape to the second (A → B), and <see cref="Depth"/> is non-negative. Primitive pairs compute
/// these exactly; mesh and box-box pairs may return an approximate contact point and depth.
/// </summary>
public readonly record struct ShapeContact(Vector3 Point, Vector3 Normal, float Depth);
