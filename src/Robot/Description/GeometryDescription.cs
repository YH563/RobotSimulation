using System.Numerics;

namespace RobotSimulation.Robot.Description;

/// <summary>
/// Abstract base for geometry (a polymorphic record union type).
/// Describes only the "shape parameters", not the pose — the pose is held by <see cref="VisualElement"/>.
/// Units are meters; primitive revolution axes are +Z (URDF/ROS semantics).
/// </summary>
public abstract record GeometryElement;

/// <summary>
/// Box geometry. ↔ URDF &lt;box&gt;
/// </summary>
/// <param name="Size">Full widths along x/y/z (the three components of the URDF size).</param>
public sealed record BoxGeometry(Vector3 Size) : GeometryElement;

/// <summary>
/// Sphere geometry. ↔ URDF &lt;sphere&gt;
/// </summary>
/// <param name="Radius">Radius (meters).</param>
public sealed record SphereGeometry(float Radius) : GeometryElement;

/// <summary>
/// Cylinder geometry, revolution axis along local +Z. ↔ URDF &lt;cylinder&gt;
/// </summary>
/// <param name="Radius">Radius (meters).</param>
/// <param name="Length">Cylinder height (along Z, meters).</param>
public sealed record CylinderGeometry(float Radius, float Length) : GeometryElement;

/// <summary>
/// Capsule geometry, axis along local +Z. ↔ URDF &lt;capsule&gt;
/// </summary>
/// <param name="Radius">Radius (meters, including the hemispherical caps).</param>
/// <param name="Length">Middle cylinder segment length (excluding caps, meters); total height = Length + 2 × Radius.</param>
public sealed record CapsuleGeometry(float Radius, float Length) : GeometryElement;

/// <summary>
/// Mesh model reference. ↔ URDF &lt;mesh&gt;
/// </summary>
/// <param name="Uri">
/// The raw reference string in the URDF (a relative path or package:// form).
/// This layer only records the reference and does not resolve files; the rendering/import layer resolves
/// it via IAssetResolver and hands it to Core/Geometry/Import to generate MeshData.
/// </param>
public sealed record MeshGeometry(string Uri) : GeometryElement;
