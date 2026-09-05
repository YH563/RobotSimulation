using System.Numerics;

namespace RobotSimulation.Robot.Description;

/// <summary>
/// 几何体抽象基类（record 多态联合类型）。
/// 仅描述"形状参数"，不含位姿——位姿由 <see cref="VisualElement"/> 持有。
/// 尺寸单位：米；图元回转轴沿 +Z（URDF/ROS 语义）。
/// </summary>
public abstract record GeometryElement;

/// <summary>
/// 长方体几何。↔ URDF &lt;box&gt;
/// </summary>
/// <param name="Size">x/y/z 三个方向的全宽（即 URDF size 的三个分量）。</param>
public sealed record BoxGeometry(Vector3 Size) : GeometryElement;

/// <summary>
/// 球体几何。↔ URDF &lt;sphere&gt;
/// </summary>
/// <param name="Radius">半径（米）。</param>
public sealed record SphereGeometry(float Radius) : GeometryElement;

/// <summary>
/// 圆柱几何，回转轴沿局部 +Z。↔ URDF &lt;cylinder&gt;
/// </summary>
/// <param name="Radius">半径（米）。</param>
/// <param name="Length">圆柱高度（沿 Z，米）。</param>
public sealed record CylinderGeometry(float Radius, float Length) : GeometryElement;

/// <summary>
/// 胶囊几何，轴沿局部 +Z。↔ URDF &lt;capsule&gt;
/// </summary>
/// <param name="Radius">半径（米，含两端半球帽）。</param>
/// <param name="Length">中间圆柱段长度（不含半球帽，米）；总高 = Length + 2 × Radius。</param>
public sealed record CapsuleGeometry(float Radius, float Length) : GeometryElement;

/// <summary>
/// 网格模型引用。↔ URDF &lt;mesh&gt;
/// </summary>
/// <param name="Uri">
/// URDF 中原始引用串（可为相对路径或 package:// 形式）。
/// 本层只记录引用、不解析文件；由渲染/导入层经 IAssetResolver 解析后交给
/// Core/Geometry/Import 生成 MeshData。
/// </param>
public sealed record MeshGeometry(string Uri) : GeometryElement;