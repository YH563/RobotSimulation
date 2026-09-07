# RobotSimulation 公共 API 约定（草案）

> 状态：草案（v0.1） · 与 `docs/architecture.md` 配套。
> 本文定义"未来作为库发布"时的 public API 表面。当前单工程内按命名空间先行约束，
> API 稳定后再机械拆分为多个 assembly / NuGet 包。

## 1. 总则

1. **门面化**：对外只暴露少量稳定的门面类型（Loader / Sink / Viewport / 数据模型），
   内部管道（GL 句柄、VBO 布局、纹理单元、SceneBuilder 细节）一律 `internal`。
2. **禁止泄漏平台类型**：任何 public 成员签名不得出现 `Silk.NET.*`、
   `OpenGL`、窗口/控件类。它们只允许存在于渲染实现层与 Host 工程。
3. **句柄代替对象**：创建可视化对象（机器人、显示项）返回 `int` 句柄，
   调用方不接触内部 `GameObject` / `Mesh`。
4. **数据模型可序列化**：`RobotSimulation.Robot.*` 描述类型是纯数据契约，
   不引用引擎与渲染类型，方便跨进程/跨语言序列化与其它系统复用。
5. **线程契约**：`IVisualizationSink` 的写入方法可在任意线程调用（内部快照），
   渲染只在渲染线程进行。除注册/释放外，调用方无需加锁。
6. **单位与坐标系契约**：
   - 世界坐标系：右手系，**Z 向上**；
   - 数值单位：米（长度）、弧度（角度/关节角）、秒（时间戳）；
   - 颜色：RGBA，分量范围 `[0,1]`（`Vector4`）；
   - 网格局部坐标：图元回转轴沿 +Z（URDF/ROS 语义）。
7. **无应用交互**：本库不提供点选/菜单/面板/测量等应用层 UI。宿主把自身输入事件翻译为
   相机命令即可（库不依赖任何输入框架；可选提供纯数据的手势适配器与示例代码，见 ADR-011/012）。

## 2. 可见性分层（包规划）

| 未来的程序集 | 内容 | 预期 public 面 |
|---|---|---|
| `RobotSimulation.RobotModel` | `Robot/Description` + `Robot/Urdf` | 门面 + 数据模型全 public，实现细节 internal |
| `RobotSimulation.Core` | `Core/Scene` + `Core/Geometry` + `Core/Rendering` | Scene/Geometry 与渲染抽象接口 public |
| `RobotSimulation.Rendering.Silk` | GL 实现 | 仅宿主需要的最小上下文类型 |
| `RobotSimulation.Visualization` | Display、Sink、Visuals | Sink / 句柄门面 public，Visual 内部 |
| `RobotSimulation.Widgets.Avalonia` 等 | 控件 | `RobotViewport` 等宿主控件 |

当前单工程结构：命名空间 `RobotSimulation.Core.*`、`RobotSimulation.Robot.*`、
`RobotSimulation.Visualization.*` 已按此预留；尚未拆程序集。

## 2.1 双 API 面（组件面 / 图形内核面）

| API 面 | 目标用户 | 公开类型 | 说明 |
|---|---|---|---|
| **A · 组件面** | 工控集成者（开箱即用） | `RobotModel`、`RobotViewport`、`IVisualizationSink`、Robot 数据模型 | URDF → 机器人显示 + 数据 Push，嵌入宿主 UI 即用 |
| **B · 图形内核面** | 需特殊处理的开发者 | `MeshData`、图元 GameObject 类（`Box`/`Sphere`/`Cylinder`/`Capsule`/`GroundPlane`）、`AssimpModelLoader`、`Scene/GameObject/Transform/Camera`、`RobotModel`（机器人 GameObject 树） | 自由生成/导入网格、自定义可视化、接自有场景树 |

- 两面同库同进程；A 构建于 B 之上（`RobotModel` 构建时内部使用图元数据与 importer）。
- 高级用法示例：用 B 的 `AssimpModelLoader` 载入模型文件 → `MeshData`，再交给 A 的 Sink/场景
  或直接用 B 渲染。两面都不属于"应用交互"（见 §1.7）。


## 3. 公共类型清单（按模块）

### 3.1 `Core/Scene`（引擎能力，public）

场景对象模型：`GameObject`（含 `Name`，支持无 Mesh 的骨架节点）、`Transform`（父子层级，
行主序/行向量，含 `LocalToWorld`/`WorldToLocal`/`GetWorldInverseMatrix`）、`SceneGraph`
（Add/Remove/Update/Pick）、`Light`、`Camera`（轨道相机：只暴露数学状态与
`Rotate/Pan/Zoom/Reset` 命令，不暴露输入框架类型；另有 `ScreenToWorldRay` 做像素反投影）。

**拾取（P0 · 图形内核面能力，不是应用层点选）**：`Camera.ScreenToWorldRay` 把宿主给的屏幕像素
转成世界射线；`SceneGraph.Pick` 返回最近命中的 `RaycastHit`（命中对象 / 世界点 / 距离 / 法线 /
重心坐标）。`GameObject.Pickable`（默认 true）可整体屏蔽某根子树（默认装配的世界/局部坐标轴、
网格地面已设为 false，避免遮挡目标的拾取）；`Pick` 的可选 `predicate` 用于按类型/link 名过滤。
本库只提供"射线 → 命中"的底层数学，**不**接管鼠标/输入框架（见 §1.7）：由宿主把自身输入事件
翻译为 `Ray` 后调用。

基础图元是 `GameObject` 派生类——`new` 即生成 CPU 几何，轴沿 +Z，参数为 URDF 语义尺寸：

| 类型 | 说明 |
|---|---|
| `Box` | 长方体贴盒；`(width, height, depth)` |
| `Sphere` | 经纬球；`(radius[, segments = 32, rings = 16])` |
| `Cylinder` | 圆柱；`(radius, length[, segments = 32])` |
| `Capsule` | 胶囊；`(radius, length[, segments = 32, rings = 8])` |
| `GroundPlane` | 水平地面（XY 平面、法线 +Z；命名避开 `System.Numerics.Plane`）；`(width, depth)` |

### 3.2 `Core/Geometry`（引擎能力，public）

| 类型 | 说明 |
|---|---|
| `MeshData` | CPU 网格数据；`AddVertex/AddTriangle/ToInterleavedArray/ToIndexArray/ComputeBounds` |
| `VertexLayout` | 交错顶点布局单一事实来源（pos3 \| uv2 \| normal3 \| tangent3） |
| `Ray` | 世界空间射线（起点 + 已归一化方向；`Direction` 单位长，射线参数 `t` 即世界距离） |
| `Bounds` | 轴对齐包围盒（`Min/Max/Center/Size`；`FromPoints`） |
| `Raycast` | 纯几何射线求交原语：`HitSphere` / `HitAABB` / `HitTriangle`（Möller–Trumbore，双面命中）/ `HitPlane` |
| `AssimpModelLoader`（`Geometry.Import`） | 模型文件 → `LoadedModel`（STL/OBJ/DAE/glTF，封装 AssimpNet）；`Load` / `LoadMeshData` |
| `LoadedModel` / `LoadedMesh` | 导入结果：平铺 submesh 列表（每个 = MeshData + MaterialData） |
| `LoadOptions` | 导入选项（FlipUvV / FlipWinding / GlobalScale） |

> `MeshImporter`/`StlImporter` 为 M2.5 规划中的"按扩展名分发门面 + 手写 STL 导入器"（尚未实现）。

### 3.3 `Core/Rendering`（引擎能力，public）

| 类型 | 说明 |
|---|---|
| `MaterialData` | PBR 材质描述（CPU：BaseColor/Metallic/Roughness + 贴图引用 + 双面/线框位） |
| `TextureReference` | 贴图引用（文件路径或内存字节 + sRGB/Linear 语义）；GPU 上传归渲染后端 |
| `IRenderer` / `IRenderContext` | 渲染抽象接口（public 面不出现 GL 类型） |

### 3.4 `Robot/Description` + `Robot/Urdf`（数据契约，public；解析内部细节 internal）

**数据模型（纯数据、无引擎引用，public）：**

`RobotDescription`（聚合根：Name/Links/Joints/FindLink/RootLinks）、`Link`、`Joint`
（含 `JointType`：Fixed/Revolute/Continuous/Prismatic）、`VisualElement`、`MaterialElement`、
`GeometryElement`（record 多态：`Box/Sphere/Cylinder/Capsule/MeshGeometry`）。
位姿以 `Matrix4x4` 表达（`Joint.Origin` / `VisualElement.LocalTransform`）。
**不含碰撞与惯性描述**——解析只覆盖可视化（见 ADR-011 范围）。

**门面与可替换点（public）：**

| 类型 | 形态 | 说明 |
|---|---|---|
| `RobotModel` | 实例 = `RobotModel : GameObject`（树根）+ 静态工厂 | 单入口：`Parse/ParseFile` 直接得到完整机器人树（可 `scene.Add`）；`new RobotModel(description)` 复用任意描述；`Description`/`CreateState()` 供 headless |
| `IAssetResolver` | 接口 | 扩展点：解析 mesh/texture 相对路径与 `package://` |
| `UrdfParser` | internal 或 public(备选) | 实现细节；如 public 需文档化为"高级用法" |

**内部不公开：** 浮点解析辅助、XML 容错细节、树校验内部算法。

### 3.5 `Visualization`（rviz-like 层，public 面最小化）

| 类型 | 说明 |
|---|---|
| `IVisualizationSink` | **核心契约**：句柄创建 + 数据 Push（任意线程） |
| `VisualizationScene`(暂名) | 门面：管理内部 Scene/Display，返回句柄 |
| `Data/*Data` | 数据容器（public，供用户构造后 Push） |

内部不公开：`Visuals/*Visual`（除非用户需自定义 Display 类型——届时暴露窄基类/接口）。

### 3.6 Widgets / Host（仅"渲染表面 + 相机"，非应用 UI）

`RobotViewport`（Avalonia/WPF 各自实现）只提供：一块可渲染的表面、`Camera`（数学状态 +
`Rotate/Pan/Zoom/Reset` 命令）、`Sink` 与 `LoadRobot`。**不包含**点选/菜单/面板/测量等应用
交互——宿主把自身框架的输入事件翻译成相机命令即可（库附可选纯数据手势适配器与示例）。
具体签名见 §4.3。

## 4. 接口签名草案

> 以下为**草案**，随 M2/M3/M4 落地固化；签名变化前应同步本文。

### 4.1 RobotModel（✅ 已固化，M2/M3）

```csharp
namespace RobotSimulation.Robot;

/// <summary>机器人：URDF/描述的单入口，同时是可入场景的 GameObject 树根。</summary>
public sealed class RobotModel : GameObject
{
    // 便捷工厂：读 URDF → 完整机器人树（一次调用即可 scene.Add / 驱动）
    public static RobotModel Parse(string urdfXml, string? baseDirectory = null, IAssetResolver? resolver = null);
    public static RobotModel ParseFile(string path, IAssetResolver? resolver = null);

    // 扩展点：任意 RobotDescription（SDF/自定义解析产物）都能构建同样的机器人树
    public RobotModel(RobotDescription description, IAssetResolver? resolver = null);

    // 纯数据描述（headless / FK / 序列化 / 多实例复用）
    public RobotDescription Description { get; }
    public string RobotName { get; }               // URDF <robot name>（GameObject.Name = 根 link 名）
    public RobotState CreateState();               // headless 关节状态 + FK

    // 关节驱动（自身即树根，scene.Add 后由渲染器遍历绘制）
    public IReadOnlyList<Joint> DrivableJoints { get; }
    public void SetJointValue(string name, float value);          // 旋转弧度/平移米
    public void ApplyJointValues(IReadOnlyList<float> values);
}
```

使用示例（也是实际代码形态）：

```csharp
var robot = RobotModel.ParseFile("arm.urdf");       // 读 URDF → 完整机器人 GameObject 树
scene.Add(robot);                                    // 与其它 GameObject 平等加入
robot.SetJointValue("shoulder", 1.2f);               // 驱动关节（旋转弧度/平移米）
```

`IAssetResolver`（扩展点，mesh/texture 路径解析）：

```csharp
public interface IAssetResolver
{
    /// <returns>解析后的绝对路径；无法解析返回 null。</returns>
    string? Resolve(string uri, string? baseDirectory);
}
```

### 4.2 Visualization Sink（M4，rviz 内核雏形）

```csharp
namespace RobotSimulation.Visualization;

/// <summary>
/// 统一的可视化数据写入协议。所有方法都可由任意线程调用（内部快照），
/// 渲染只在渲染线程进行。时间戳单位秒，坐标系 Z-up，颜色 RGBA∈[0,1]。
/// </summary>
public interface IVisualizationSink
{
    // ---- 机器人（句柄化）----
    int  CreateRobot(RobotDescription robot, string? name = null);
    void SetJointAngles(int handle, IReadOnlyList<float> angles, double stamp);
    void RemoveRobot(int handle);

    // ---- 通用显示项（按主题名管理生命周期）----
    void PushLineStrip(string topic, IReadOnlyList<Vector3> points, Vector4 color, double stamp);
    void PushPointCloud(string topic, IReadOnlyList<Vector3> points,
                        IReadOnlyList<Vector4>? colors, double stamp);
    void PushFrame(string topic, Matrix4x4 pose, double stamp);   // 坐标系/位姿
    void Clear(string topic);
    void ClearAll();
}
```

### 4.3 Widgets / Viewport（M6，宿主控件）

```csharp
namespace RobotSimulation.Widgets;

public sealed class RobotViewport   // 仅渲染表面 + 相机命令 + Sink（不含交互 UI，草案）
{
    public IVisualizationSink Sink { get; }
    public Camera Camera { get; }               // 数学状态与命令；输入手势由宿主翻译后调用

    public int  LoadRobotFromFile(string path);
    public int  LoadRobot(RobotDescription robot);
}
```

## 5. 反例清单（避免公开）

| 反例 | 原因 |
|---|---|
| public 方法返回/接收 `GL`、`Mesh`、`ShaderProgram`、窗口句柄 | 破坏跨 UI、跨后端可移植性（违反 §1.2） |
| public 暴露 `Scene` 内部节点让用户直接改 `GameObject.Transform` 作为"正规用法" | 绕过句柄/快照机制，线程不安全 |
| 让用户直接 new `RobotVisual`/`PointCloudVisual` 并管理 GL 资源 | 生命周期与渲染线程职责泄漏 |
| 在数据模型里放 GL/UI 字段 | 模型必须可序列化、可 headless |
| 公开带 `DateTime`/系统时钟的 stamp | 统一使用 `double`（秒），便于与仿真/ROS 时间对齐 |
| 库内实现点选/右键菜单/属性面板/测量等应用交互 | 交互属宿主 UI 框架职责；组件保持纯显示 + 接口（ADR-011） |

## 6. 维护约定

- 每完成一个里程碑（M2~M6），把签名草案中的对应节标记"✅ 已固化"，并记录变更。
- 增加 public 类型前先问：它是门面/契约/数据模型，还是内部管道？后者一律 internal。
- 本文与 `docs/architecture.md` 同步评审；两者冲突时以 API 文档描述为准说明原因。
