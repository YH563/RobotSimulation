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
| `RobotSimulation.Core` | `Core/Geometry`, `Scene`, 渲染**抽象** | Geometry/Scene public；渲染抽象接口 public |
| `RobotSimulation.Rendering.Silk` | GL 实现 | 仅宿主需要的最小上下文类型 |
| `RobotSimulation.Visualization` | Display、Sink、Visuals | Sink / 句柄门面 public，Visual 内部 |
| `RobotSimulation.Widgets.Avalonia` 等 | 控件 | `RobotViewport` 等宿主控件 |

当前单工程结构：命名空间 `RobotSimulation.Core.*`、`RobotSimulation.Robot.*`、
`RobotSimulation.Visualization.*` 已按此预留；尚未拆程序集。

## 2.1 双 API 面（组件面 / 图形内核面）

| API 面 | 目标用户 | 公开类型 | 说明 |
|---|---|---|---|
| **A · 组件面** | 工控集成者（开箱即用） | `RobotModel`、`RobotViewport`、`IVisualizationSink`、Robot 数据模型 | URDF → 机器人显示 + 数据 Push，嵌入宿主 UI 即用 |
| **B · 图形内核面** | 需特殊处理的开发者 | `MeshData`、`PrimitiveBuilder`、`MeshImporter`、`Scene/GameObject/Transform/Camera`、`RobotModel`/`RobotGameObject` | 自由生成/导入网格、自定义可视化、接自有场景树 |

- 两面同库同进程；A 构建于 B 之上（`RobotModel.Instantiate` 内部使用图元数据与 importer）。
- 高级用法示例：用 B 的 `MeshImporter` 载入私有格式 → `MeshData`，再交给 A 的 Sink/场景
  或直接用 B 渲染。两面都不属于"应用交互"（见 §1.7）。


## 3. 公共类型清单（按模块）

### 3.1 `Core/Geometry`（引擎能力，public）

| 类型 | 说明 |
|---|---|
| `MeshData` | CPU 网格数据；`AddVertex/AddTriangle/ToInterleavedArray/ToIndexArray` |
| `PrimitiveBuilder` | 静态图元生成（纯函数）；`CreateBox/Sphere/Cylinder/Capsule/Plane`，轴沿 +Z |
| `MeshImporter` | 门面：模型文件 → `MeshData`（按扩展名分发） |
| `StlImporter` | STL（binary + ascii）→ `MeshData` |

这些类型是"引擎能力"而非机器人专用，保持 public，允许宿主/用户自定义扩展几何与格式接入
（通过 `MeshImporter` 门面扩展后续 OBJ/DAE/glTF，或封装 AssimpNet）。

### 3.2 `Core/Scene`（引擎能力，public）

`Scene`、`GameObject`、`Transform`、`Camera` 均为 public。注意：Camera 只暴露数学状态
与命令（`Rotate/Pan/Zoom/Reset`），不暴露具体输入框架类型。

### 3.3 `Robot/Description` + `Robot/Urdf`（数据契约，public；解析内部细节 internal）

**数据模型（纯数据、无引擎引用，public）：**

`RobotDescription`（聚合根：Name/Links/Joints/FindLink/RootLinks）、`Link`、`Joint`
（含 `JointType`：Fixed/Revolute/Continuous/Prismatic）、`VisualElement`、`MaterialElement`、
`GeometryElement`（record 多态：`Box/Sphere/Cylinder/Capsule/MeshGeometry`）。
位姿以 `Matrix4x4` 表达（`Joint.Origin` / `VisualElement.LocalTransform`）。
**不含碰撞与惯性描述**——解析只覆盖可视化（见 ADR-011 范围）。

**门面与可替换点（public）：**

| 类型 | 形态 | 说明 |
|---|---|---|
| `RobotModel` | 静态工厂 + 实例 | 单入口：`Parse/ParseFile`（持有 `Description`）；`Instantiate()`（无渲染参数）返回 `RobotGameObject`；`CreateState()` 返回 headless FK |
| `IAssetResolver` | 接口 | 扩展点：解析 mesh/texture 相对路径与 `package://` |
| `UrdfParser` | internal 或 public(备选) | 实现细节；如 public 需文档化为"高级用法" |

**内部不公开：** 浮点解析辅助、XML 容错细节、树校验内部算法。

### 3.4 `Visualization`（rviz-like 层，public 面最小化）

| 类型 | 说明 |
|---|---|
| `IVisualizationSink` | **核心契约**：句柄创建 + 数据 Push（任意线程） |
| `VisualizationScene`(暂名) | 门面：管理内部 Scene/Display，返回句柄 |
| `Data/*Data` | 数据容器（public，供用户构造后 Push） |

内部不公开：`Visuals/*Visual`（除非用户需自定义 Display 类型——届时暴露窄基类/接口）。

### 3.5 Widgets / Host（仅"渲染表面 + 相机"，非应用 UI）

`RobotViewport`（Avalonia/WPF 各自实现）只提供：一块可渲染的表面、`Camera`（数学状态 +
`Rotate/Pan/Zoom/Reset` 命令）、`Sink` 与 `LoadRobot`。**不包含**点选/菜单/面板/测量等应用
交互——宿主把自身框架的输入事件翻译成相机命令即可（库附可选纯数据手势适配器与示例）。
具体签名见 §4.3。

## 4. 接口签名草案

> 以下为**草案**，随 M2/M3/M4 落地固化；签名变化前应同步本文。

### 4.1 RobotModel（✅ 已固化，M2/M3）

```csharp
namespace RobotSimulation.Robot;

/// <summary>机器人单入口：解析 + 持有描述 + 创建场景对象。</summary>
public sealed class RobotModel
{
    // ① 解析（静态）
    public static RobotModel Parse(string urdfXml, string? baseDirectory = null, IAssetResolver? resolver = null);
    public static RobotModel ParseFile(string path, IAssetResolver? resolver = null);

    // ② 描述（纯数据，headless / FK / 序列化）
    public RobotDescription Description { get; }
    public string? Name { get; }
    public RobotState CreateState();               // headless 关节状态 + FK

    // ③ 场景对象（纯数据、无渲染参数）——返回单个 GameObject，由调用方 scene.Add
    public RobotGameObject Instantiate();
}

/// <summary>机器人在场景中的根节点：RobotGameObject : GameObject，与其它对象平等。</summary>
public sealed class RobotGameObject : GameObject
{
    public string RobotName { get; }
    public IReadOnlyList<Joint> DrivableJoints { get; }
    public void SetJointValue(string name, float value);          // 旋转弧度/平移米
    public void ApplyJointValues(IReadOnlyList<float> values);
}
```

使用示例（也是实际代码形态）：

```csharp
var model   = RobotModel.ParseFile("arm.urdf");      // ① 解析（持有 RobotDescription）
var robotGo = model.Instantiate();                   // ② 场景对象（纯数据，任意线程）
scene.Add(robotGo);                                   // ③ 与其它 GameObject 平等加入
robotGo.SetJointValue("shoulder", 1.2f);             // ④ 驱动关节
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
