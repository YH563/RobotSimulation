# RobotSimulation 架构设计（简体中文）

> 状态：v0.1 反映当前实现。配套：`../../README.zh-CN.md`、`../core/zh-CN.md`、`../robot/zh-CN.md`、`../opengl/zh-CN.md`。

本文按「可发布 NuGet 库」的视角描述整体架构：包边界、依赖方向、运行时/渲染线程模型、关键设计决策（ADR）与扩展点。

---

## 1. 目标与设计原则

**目标**

- 通过 URDF 解析与模型加载生成几何与机器人场景。
- 提供曲线、点云、坐标轴、图元等可视化原语。
- 渲染核心与 UI 框架解耦，可嵌入 Avalonia / WPF / 裸窗口。
- `Core` / `Robot` 不依赖任何 Silk.NET / OpenGL 类型；`OpenGL` 是唯一后端实现。
- 同一套数据既可 headless 计算，也可渲染。

**设计原则（按优先级）**

1. **单一依赖方向**：宿主 → `OpenGL`/`Robot` → `Core`，禁止反向引用。
2. **领域与框架分离**：`Core` 不识别「机器人」；机器人语义只在 `Robot` 层。
3. **库边界第一**：public API 从第一版就按「未来作为库发布」约束。
4. **纯函数静态化、有状态系统接口化**：纯计算用静态方法 + 显式 options；可替换/需持有生命周期的用接口与实例。
5. **跨 UI 可移植**：public 签名永不出现 Silk/GL/窗口类型；渲染后端与宿主可整体替换。

---

## 2. 包与层级

```text
┌─────────────────────────────────────────────────────────────────┐
│  (test) BareWindowTest / AvaloniaTest — exe, 窗口/输入, 组装根         │
│          references: Core, Robot, OpenGL（Avalonia 宿主另加）         │
└───────────────┬─────────────────────────────────────────────────┘
                │
   ┌────────────┴────────────┐
   │                         │
   ▼                         ▼
┌──────────────────┐   ┌──────────────────┐
│  RobotSimulation │   │  RobotSimulation │
│  .Robot          │   │  .OpenGL         │
│  (URDF/描述/状态) │   │  (Silk.NET 后端) │
│  refs: Core      │   │  refs: Core      │
└────────┬─────────┘   └────────┬─────────┘
         │                      │
         └──────────┬───────────┘
                    ▼
        ┌──────────────────────────┐
        │  RobotSimulation.Core    │
        │  引擎内核: Scene/Geometry │
        │  /Rendering 接口 /Utils   │
        │  (no GL, no UI)          │
        └──────────────────────────┘
```

| 库 / Package | 对外职责 / Public responsibility | 依赖 / Depends on |
|---|---|---|
| `Core` | 场景对象模型、纯 CPU 数据、渲染抽象接口、点云/模型导入、工具 | `AssimpNet`、`Microsoft.Extensions.Logging`（`Logger`） |
| `Robot` | 机器人描述 / URDF / headless FK / `RobotModel` 树 | `Core` |
| `OpenGL` | 唯一渲染后端（设备层 + 渲染器 + GPU 资源 + 着色器） | `Core` |

**规则**

1. `Core` 不得引用 `Robot` / `OpenGL` / 任何 UI 框架类型。
2. `Robot` 仅引用 `Core`（用于 `MeshData` / `MaterialData` / `Scene` 等 CPU 数据）与纯 `System.Numerics`。
3. 所有 `Silk.NET.*` / `StbImageSharp` 只在 `OpenGL`（及宿主测试的窗口/输入）出现，永不出现在 public 签名。
4. `AssimpNet` 是 `Core` 模型导入的非托管依赖；它使 `Core` 在发布时带上原生资产——如需「纯托管轻内核」，可将其下沉为导入子包（见第 7 节取舍）。

> 依赖说明：`Core` 的 `Logger`（`Utils`）使用完整 `Microsoft.Extensions.Logging`（内部 `LoggerFactory`），而非仅 `Abstractions`。

---

## 3. 各包内部结构

**`Core/`**
```text
Scene/       GameObject, Transform, SceneGraph, Camera, Light, 图元(GameObject 派生)
Geometry/    Primitives(internal), Raycast/Bounds/Intersect, 点云 PointCloud2Data/PointCloudIo,
             Import/ (AssimpModelLoader, LoadOptions, LoadedModel, AssimpNative)
Rendering/   IRenderContext, IRenderer, MaterialData, TextureReference/TextureColorSpace, RenderPassKind
Utils/       GameTimer, Logger, MathUtils
```

**`Robot/`**
```text
Description/ RobotDescription, Link/Visual/Geometry/Joint 等纯数据; GeometryDescription
Urdf/        UrdfParser(internal), IAssetResolver/FileSystemAssetResolver, UrdfLocator, UrdfParseException
State/       RobotState (headless FK)
Model/       RobotModel (GameObject 树根 + 工厂/驱动接口)
```

**`OpenGL/`**
```text
Device/      GraphicsFactory(静态组合根), GraphicsContext(IRenderContext)
Rendering/   Renderer(IRenderer), RenderPassKind 分发, 灯光/高亮管理
Resources/   Mesh, LineMesh, PointMesh, Material, Texture2D, ShaderProgram, EmbeddedShaders
Shaders/     Model/Line/Point/Skybox/Axes 的 .vert/.frag（作为嵌入式资源打包）
```

---

## 4. 关键架构决策（ADR）

| 编号 | 决策 | 说明 |
|---|---|---|
| ADR-001 | 世界坐标系 **Z-up**（右手系），相机 up = +Z | 与 URDF/ROS/rviz 一致 |
| ADR-002 | 图元回转轴统一沿 +Z（URDF/ROS 语义） | `Cylinder`/`Capsule`/`Sphere`/`Cone` |
| ADR-003 | `Core/Geometry` 使用纯 CPU `MeshData`，与 GL 解耦 | `MeshData → Mesh` 仅在渲染线程上传 |
| ADR-004 | 图元网格生成收敛于 `internal Primitives`（非公共 API） | 不暴露可变全局状态 |
| ADR-005 | `Robot` 领域模块独立于 `Core`（`Core` 永不引用领域类型） | 领域/框架边界 |
| ADR-006 | `Robot/Description` + `Robot/Urdf` 为零渲染依赖纯数据子集 | headless / 物理 / 独立库的切割线 |
| ADR-007 | 渲染后端抽象：`Core.Rendering` 只定义接口（`IRenderContext` / `IRenderer`），Silk/GL 在 `OpenGL` 实现 | 可换后端而不动核心 |
| ADR-008 | 场景为纯数据；GPU 资源生命周期归 `Renderer`（含缓存与释放） | 领域层零渲染依赖 |
| ADR-009 | 基础图元以 GameObject 派生类暴露（`Box`/`Sphere`/`Cylinder`/`Capsule`/`GroundPlane`/`Arrow`/`Axes`） | `new Box(...)` 直接 `scene.Add` |
| ADR-010 | 输入事件只在宿主层翻译为相机命令；`Core` 不感知具体输入框架 | 相机手势策略归宿主 |
| ADR-011 | 宿主经 `OpenGL/Device/GraphicsFactory` 组装后端（接口化组合根） | 宿主只持有 `IRenderContext` / `IRenderer` |
| ADR-012 | 坐标轴恒定屏幕尺寸由 `Axes` 自身管理，Renderer 只读取 | 着色器做各向同性补偿 |
| ADR-013 | 相机是「显示状态对象」，暴露数学状态与 `Rotate`/`Pan`/`Zoom`/`Reset`，另有 `ScreenToWorldRay` | 拾取入口 |
| ADR-014 | 点云采用 ROS `sensor_msgs/PointCloud2` 风格字段布局 | `PointCloud2Data` |
| ADR-015 | 机器人子树在构建后被 `Transform` 只读锁定，只能经 `RobotModel` 内置接口改位姿 | 禁止外部直接改写关节/子 link 位姿 |
| ADR-016 | `RobotState` 与 `RobotModel` 分离：前者纯 headless FK，后者驱动可视化树 | 两者共用同一 `RobotDescription` |
| ADR-017 | 着色器管线作为后端内建资源（`EmbeddedShaders`），`Core` 只约定 `RenderPassKind` | 像游戏引擎一样可扩展/替换着色，但不强制完整引擎 |
| ADR-018 | 通过组合根（宿主）提供 `GL`，后端以接口交付；宿主仅持接口与视口尺寸 | 宿主可整体切换（裸窗口 / WPF / Avalonia） |

---

## 5. 运行时与线程模型

```text
                host UI/更新线程              host 渲染线程
               ─────────────────           ─────────────────
update loop:   scene.Update(dt)  ──► (纯数据，改 Transform/关节值)
               robot.SetJointValue(...)   │
                                          ▼
render loop:                        renderer.Render(scene)
                                    ├─ 遍历 scene.Roots
                                    ├─ 按 PassKind 分发 (Model/Line/Point/Axes)
                                    ├─ 数据→GPU 资源 缓存/创建（Mesh/Material/Texture/Shader）
                                    └─ 绘制
```

- **数据可以任意线程写**：`GameObject` / `Transform` / `MeshData` / `MaterialData` 等为纯数据，可在任意线程构建、随后在渲染前被读取。
- **只有渲染线程碰 GL / GPU 资源**：`Renderer` 拥有所有网格/材质/纹理缓存与释放；`IRenderContext.Clear` 也在渲染线程调用。
- **更新线程只写 CPU 数据**，绝不能调用任何 GL 类型。`SceneGraph.Update(deltaTime)` 会递归调用各 `GameObject.Update`。
- **`Logger` 任意线程可用**：进程级静默门面；宿主在组合根用 `Logger.Initialize(...)` 附加 provider。
- **`GameTimer`** 是后台线程固定帧率滴答器（模拟/更新用），不用于渲染循环本身。
- **`FrameStats` / `GraphicsDeviceInfo` 均为纯数据**：后端在渲染线程更新 `IRenderer.Stats`，并在设备上下文创建时一次性填充 `IRenderContext.DeviceInfo`；宿主仅用于显示（在自身选择的线程上读取即可）。

---

## 6. 扩展点

| 扩展点 | 位置 | 目的 |
|---|---|---|
| `IRenderContext` / `IRenderer` | `Core.Rendering` | 换渲染后端而不动核心/领域层 |
| `IAssetResolver` | `Robot.Urdf` | 自定义 URDF mesh/texture 引用解析（默认 `FileSystemAssetResolver`） |
| `RobotModel(RobotDescription, ...)` 构造 | `Robot` | 任意描述源（SDF/自定义）→ 机器人树 |
| `GameObject.Update` 虚方法 | `Core.Scene` | 每帧更新逻辑（轨迹、动画等） |
| `GameObject.Pickable` / 拾取 `predicate` | `Core.Scene` | 参与/过滤拾取 |
| `EmbeddedShaders` / `ShaderProgram` | `OpenGL.Resources` | 自定义/替换 GLSL 管线（模型/线/点云/坐标轴） |

---

## 7. 打包与演进

1. **当前**：单仓库 + 命名空间分区；`src/{Core,Robot,OpenGL}` 即未来程序集/包边界。
2. **API 稳定后**：按本目录 `core`/`robot`/`opengl` 各 API 文档机械拆分为多个 assembly / NuGet 包；补齐 `PackageId` / `Version` / `readme` 等元数据。
3. **数据接入**：先同进程接外部写入协议（Sink）打通；ROS2 桥接作为独立可选工程。
4. **测试宿主（三宿主测试项目）**：裸窗口、WPF、Avalonia 三个宿主示例/测试项目——每个内置一个 `RobotViewport` 式控件，既证明本库**能在不同桌面框架下独立渲染显示**，也把相关测试收编进去；仅作验证，不随库发布。它们共享同一 `Core`/`Robot`/`OpenGL` 链路，宿主差异只在「如何拿 `GL` 与如何同步视口」。**当前进展**：`src/BareWindowTest`（完全不含 UI 框架；同时是自动化冒烟测试，`--smoke [frames]` → 退出码 0/1）与 `src/AvaloniaTest`（`RobotViewportControl : OpenGlControlBase`，经 `GL.GetApi(gl.GetProcAddress)` 取 `GL`，并画进 Avalonia 按控件下发的帧缓冲）已完成；WPF 待做。两者命令形态一致——只有 `--smoke [frames]` 一个开关；加载哪些数据写在各自源码里，来自各自的 `Assets/` 目录（URDF 模型、mesh、点云），由各自工程文件拷到可执行文件旁。因此两者的控制台输出可逐行对比，两个宿主互为交叉校验，而不只是两个独立演示。
5. **可选**：为满足「纯托管轻内核」可将 `AssimpNet` 下沉为独立导入子包。

---

## 8. 维护约定

- 重大架构变更在此记录对应 ADR（增补行），保留历史决策并标注取代关系。
- 里程碑状态随实现推进更新；「现状」小节只反映实际代码。
- 本文件与 `core`/`robot`/`opengl` 各模块文档同步评审（中英两版一致）。
