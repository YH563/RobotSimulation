# RobotSimulation 架构设计

> 状态：草稿（v0.1） · 关联工程：`RobotSimulation/RobotSimulation.csproj`（net10.0, Silk.NET 2.23）

## 1. 目标与设计原则

本项目定位为"自研、无 ROS 强依赖、可嵌入桌面 UI 的机器人可视化仿真框架"，远期形态对标 rviz：

- 通过 URDF 解析 / 模型加载生成几何与机器人场景；
- 提供曲线、点云、坐标轴、Marker 等可视化原语；
- 对外保留**纯数据写入接口**（关节角、点云、路径、位姿帧），不依赖 ROS2；
- 渲染核心与 UI 框架解耦，可嵌入 Avalonia / WPF，并优于 Web 方案的性能与可控性。

设计原则（按优先级）：

1. **单向依赖**：`Host → Robot/Visualization → Core`，禁止反向引用。
2. **领域与框架分离**：`Core` 不认识"机器人"；机器人语义只在领域层。
3. **库边界第一**：从第一个里程碑起就按"未来要作为库发布"约束 public API（见 `api.md`）。
4. **无状态函数静态化、有状态系统接口化**：纯计算（图元生成、数学工具）用静态方法 + 显式 options；需要替换/注入/持有生命周期的东西（加载器、Sink、渲染后端）用接口与实例。
5. **跨 UI 可移植**：public 签名中永不出现 Silk/GL/窗口类型；渲染后端与宿主可整体替换。

**组件化边界（本库 ≠ 完整应用）**：本库只交付"仿真显示内核 + 数据接口"，**不实现任何应用层
交互 UI**（点选/高亮、右键菜单、显示开关树、测量、属性面板等）。这些属于使用方各自 UI 框架
的职责；库只保留可编程入口——相机命令（`Rotate/Pan/Zoom/Reset`）、`Sink`、场景/显示管理。
相机"手势"（把输入事件翻译成相机命令）不硬编码进库，仅提供可选输入适配器与示例代码。


## 2. 关键架构决策（ADR 摘要）

| 编号 | 决策 | 理由 / 备注 |
|---|---|---|
| ADR-001 | 世界坐标系采用 **Z-up**（右手系），相机 up = +Z | 与 URDF/ROS/rviz 语义一致；URDF 圆柱/胶囊轴沿 +Z 可直接竖直，无需适配旋转 |
| ADR-002 | 图元回转轴统一沿 +Z（URDF/ROS 几何语义） | `PrimitiveBuilder.CreateCylinder/Capsule/Sphere` |
| ADR-003 | `Core/Geometry` 使用纯 CPU `MeshData`，与 GL 解耦 | `MeshData → Mesh(GL)` 仅在渲染线程上传 |
| ADR-004 | 几何图元生成用**静态类**（纯函数）；后续以显式 `options`/窄接口扩展，不引入全局可变状态 | 参考 `PrimitiveBuilder` |
| ADR-005 | `Robot` 领域模块独立于 `Core`（一级目录），`Core` 永不引用领域类型 | 领域/框架边界；将来可独立拆包 |
| ADR-006 | `Robot/Description` + `Robot/Urdf` 为**零渲染依赖**的纯数据子集 | 是未来 headless/物理/独立库的切割线 |
| ADR-007 | 渲染后端需要抽象（`IRenderDevice` 族），Silk/GL 只允许存在于实现层 | 前置条件：可换 Avalonia/WPF 宿主而核心不动（M5） |
| ADR-008 | 对外数据更新统一走 **Push 型 Sink 协议**，内部双缓冲快照 | 外部线程可安全写入，渲染线程消费；不暴露锁 |
| ADR-009 | 调用者通过**句柄（int id）**引用可视化对象，而非内部 `GameObject` | 保持实现可演进，API 稳定 |
| ADR-010 | 输入事件只在 Host 层翻译为相机命令，Core 不感知具体 UI/输入框架 | 现 `Program.cs` 已遵循 |
| ADR-011 | **交互层 out-of-scope**：库仅含显示内核 + 数据接口；点选/菜单/面板/测量等 UI 交互由宿主框架实现 | 组件化哲学：降低第三方集成难度、保留 UI 自由度；库只暴露相机命令 / Sink / 场景管理等编程入口 |
| ADR-012 | 相机是库内"显示状态对象"；相机**手势**以可选适配器/示例提供，不硬编码进库 | 相机能力属于显示，手势策略属于交互（宿主可自选或自写） |
| ADR-013 | 提供**双 API 面**：① 组件面（`RobotModel`/`RobotViewport`/`Sink`，开箱即用）② 图形内核面（`MeshData`/`PrimitiveBuilder`/`MeshImporter`/`Scene`/`Camera` 等，供特殊处理） | 组件面服务工控集成，内核面保留高级自由度；同库同进程，组件面构建于内核面之上 |
| ADR-014 | 3D 模型文件解析（STL/OBJ/… → `MeshData`）属 **Core** 纯 CPU 引擎能力，放 `Core/Geometry/Import`，与机器人/渲染无关 | 模型加载与 URDF 解耦；STL 手写，DAE/glTF 可选封装 AssimpNet；`IAssetResolver` 仍留在 Robot/Urdf 消费 URDF 路径语义 |
| ADR-015 | **GameObject 数据化**：节点只持 CPU `MeshData`/`MaterialData`，不管理 GPU 资源；`Renderer` 在渲染时实例化 GPU 并缓存 | 创建机器人/场景对象不需要任何渲染上下文；GPU 生命周期归 Renderer；领域层零渲染依赖 |

## 2.1 范围边界（In / Out of Scope）

**In（本库提供）：**

- URDF 解析与机器人/几何场景构建；
- 3D 模型文件导入（STL/OBJ/… → `MeshData`，`Core/Geometry/Import`）；
- 渲染显示能力：图元/模型/线条/点云/坐标轴等可视化原语；
- 相机：数学状态 + 轨道命令（`Rotate/Pan/Zoom/Reset`）；
- 数据接口：`RobotModel`（静态 Parse/ParseFile + Instantiate）、`IVisualizationSink`（任意线程 Push、句柄管理）；
- 渲染后端抽象与宿主所需的"渲染表面"控件外壳（Avalonia/WPF，M6）。

**Out（宿主 UI 框架职责）：**

- 鼠标/键盘事件采集与手势策略（可选用库提供的适配器/示例）；
- 点选拾取后的高亮、右键菜单、属性面板、显示开关树、测量等应用交互；
- 应用主窗口、工具栏、业务面板。


## 3. 当前代码现状（截至 v0.1）

```
RobotSimulation/                            # 仓库根
├── RobotSimulation.sln                     # 四个工程（依赖方向由工程引用强制）
├── src/
│   ├── Core/          RobotSimulation.Core.csproj      # 引擎内核（领域无关，禁止引用 Robot/OpenGL）
│   │   ├── Geometry/                         # 纯 CPU 几何（已就位）
│   │   │   ├── MeshData.cs                   # 顶点并行存储 + 交错导出
│   │   │   └── PrimitiveBuilder.cs           # Box/Sphere/Cylinder/Capsule/Plane（Z-up 图元轴）
│   │   ├── Scene/                            # 场景图
│   │   │   ├── GameObject.cs                 # 含 Name；支持无 Mesh 的骨架节点
│   │   │   ├── Transform.cs                  # 父子层级（行主序/行向量约定）
│   │   │   ├── Scene.cs                      # Add/Remove/Update（空节点不阻断子树）
│   │   │   └── Camera.cs                     # 轨道相机（世界 Z-up）
│   │   ├── Rendering/                        # 纯 CPU 数据描述（不引用 GL）
│   │   │   └── MaterialData.cs               #   材质描述（MeshData 在 Geometry/）
│   │   └── Utils/  MathUtils.cs  GameTimer.cs
│   ├── Robot/         RobotSimulation.Robot.csproj     # 机器人领域（引用 Core，不被 Core 引用）
│   │   ├── Description/  RobotDescription/Link/Joint/VisualElement/几何族（零渲染依赖）
│   │   ├── Urdf/         UrdfParser/AssetResolver（零渲染依赖）
│   │   ├── RobotModel.cs / RobotGameObject.cs          # URDF 单入口 → RobotGameObject(: GameObject)
│   │   └── State/       RobotState.cs                  # headless FK（关节角 → link 全局位姿）
│   ├── OpenGL/        RobotSimulation.OpenGL.csproj   # 渲染实现层（引用 Core；命名空间 RobotSimulation.OpenGL）
│   │   ├── GraphicsContext.cs  ShaderCache.cs  ShaderProgram.cs
│   │   ├── Mesh.cs                       # 固定布局 pos3|uv2|n3|t3，DrawElements(Triangles)
│   │   ├── Renderer.cs                   # 遍历场景 → GPU 实例化与绘制（GL 生命周期归属本层）
│   │   └── Material.cs  Texture2D.cs
│   └── Host/          RobotSimulation.Host.csproj      # exe：窗口 + 输入 + 装配（引用全部）
│       ├── Program.cs                    # Host：窗口 + 输入（Silk.NET.Windowing）
│       └── Assets/Shaders/Model/Standard.*             # Blinn-Phong 光照管线
└── docs/                                  # 本目录：architecture.md / api.md
```

交互状态：世界 **Z-up**；鼠标采用 **rviz 风格**（左键旋转、中键平移、右键拖拽/滚轮缩放）。

依赖方向（当前已由工程引用强制，不再依赖纪律）：
`Core ← Robot ← Host`；`Core ← OpenGL ← Host`。渲染实现层（OpenGL）尚未抽象为
`IRenderDevice`（M5 未完成）——现阶段它作为独立工程存在，public 面仍含 GL 类型，见 §6。

## 4. 目标目录树（本设计的落点）

> 注：工程化拆分后的现状见 §3（Core / Robot / OpenGL / Host 四个工程）。
> 下面是语义边界规划——新增的 Visualization / Rendering 抽象等在各自工程中落地，
> 目录结构沿用本规划的模块划分。

```
RobotSimulation/
├── src/
│   ├── Host/                          # Host（未来可替换为 AvaloniaHost / WpfHost）
│   │   └── Assets/Shaders/…
│   ├── Core/                           # 引擎内核（领域无关，禁止引用 Robot/Visualization）
│   │   ├── Geometry/                   # MeshData / PrimitiveBuilder /（STL 等导入）
│   │   └── Import/                 # 模型文件 → MeshData（纯 CPU，无 GL/UI）
│   │       ├── MeshImporter.cs     #   门面：按扩展名分发
│   │       └── StlImporter.cs      #   STL（binary + ascii）
│   ├── Math/ (或并入 Utils)
│   ├── Scene/                      # Scene / GameObject / Transform / Camera
│   ├── Rendering/                  # Renderer（GPU 实例化与绘制）+ CPU 资源 MeshData/MaterialData
│   │                               #   未来抽象化 IRenderDevice（M5），Silk 实现进独立 backend
│   └── Utils/
├── Robot/                          # 机器人领域模块（引用 Core，不被 Core 引用）
│   ├── Description/                # 纯数据（零渲染依赖）：RobotDescription 聚合根 +
│   │                               #   Link/Joint/VisualElement/MaterialElement/几何族
│   ├── Urdf/                       # UrdfParser/Options/Exception/AssetResolver（零渲染依赖）
│   ├── RobotModel.cs / RobotGameObject.cs        # URDF 单入口：RobotModel 静态解析持有描述，
│   │                               #   Instantiate() → RobotGameObject(: GameObject)，
│   │                               #   与场景中其它对象平等；关节驱动内置
│   └── State/                      # (M3+) RobotInstance、关节状态、句柄管理
├── Visualization/                  # (M4+) rviz-like Display
│   ├── Data/                       # PointCloudData / PathData / FrameData …
│   ├── Visuals/                    # RobotVisual / LineStripVisual / PointCloudVisual / FrameVisual
│   └── IVisualizationSink.cs       # 统一外部写入协议（句柄 + Push）
└── docs/                           # 本目录：architecture.md / api.md
```

命名空间与目录一一对应：`RobotSimulation.Core.*`、`RobotSimulation.Robot.*`、
`RobotSimulation.Visualization.*`。

## 5. 分层依赖与规则

依赖方向（仅允许向下引用）：

```
Program.cs (Host) ──▶ Robot ──▶ Core
        │                │
        └──────────────▶ Visualization ──▶ Core (+ RobotModel 纯数据子集)
                         │
                   Rendering 后端抽象 ⇦ Silk/GL 实现（未来独立程序集）
```

规则：

1. `Core` 不允许引用 `Robot` / `Visualization` / 任何 UI 框架类型。
2. `Robot/Description` 与 `Robot/Urdf` 不允许引用 `Core.Scene` / `Core.Rendering`
   （唯一引用点是纯 `System.Numerics`），从而保留 headless 复用能力。
3. `Robot/RobotModel.Instantiate()` 构建纯数据场景对象；领域层不再感知渲染——GPU 实例化统一收敛于 `Core/Rendering/Renderer`（渲染线程）。
4. `Visualization` 通过**句柄**管理内部对象；其 `Sink` 协议对任意来源（自身仿真、
   ROS2 桥接、回放文件）开放。
5. 一切 `Silk.NET.*`/窗口类型只允许出现在 Host 与渲染后端实现；任何 public 签名不含它们。
6. 渲染只发生在渲染线程；`Sink` 写入支持任意线程（内部双缓冲快照）。

## 6. 里程碑与现状

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M0 | Scene 渲染空节点修复；`GameObject.Name` | ✅ 已完成 |
| M1 | `Core/Geometry`：MeshData + PrimitiveBuilder 五图元；同屏展示 | ✅ 已完成 |
| M1.5 | 世界 Z-up（Camera/Plane/demo）；rviz 风格鼠标（左旋/中移/右缩） | ✅ 已完成 |
| M2 | `Robot/Description` + `Robot/Urdf` 解析 + `RobotModel` 静态解析门面 + 控制台验证 | ✅ 已完成 |
| M2.5 | `Core/Geometry/Import`：STL importer（binary+ascii）→ `MeshData`；`MeshImporter` 门面 | ⬜ |
| M3 | `RobotModel.Instantiate() → RobotGameObject`（单个 GameObject，纯数据无渲染参数，scene.Add 平等）+ 关节驱动 demo | ✅ 已完成 |
| M3.5 | 渲染数据化：`GameObject` 只持 `MeshData`/`MaterialData`（CPU），新增 `Renderer` 负责 GPU 实例化与绘制 | ✅ 已完成 |
| M4 | `Visualization`：线条/点云数据通道 + `IVisualizationSink`（rviz 内核雏形） | ⬜ |
| M5 | `Core.Rendering` 抽象化（`IRenderDevice` 族），Silk/GL 收进实现层 | ⬜ |
| M6 | Avalonia/WPF Host 实验工程：验证组件嵌入（渲染表面 + 相机命令），不含交互 UI | ⬜ |

## 7. 演进 / 拆包路线

1. **当前阶段**：单工程 + 命名空间分区；目录即未来程序集边界；
   `Robot/Description+Urdf` 保持零渲染依赖作为切割线。
2. **API 稳定后**：按 `docs/api.md` 的包规划机械拆分程序集
   （RobotModel → Core(+抽象) → Rendering.Silk → Visualization → Widgets）。
3. **接入外部数据**：先在同一进程 Push 打通；ROS2 桥接作为独立可选工程实现 `IVisualizationSink`。

## 8. 本文件维护约定

- 重大架构变更在此记录对应 ADR（增补行），不删除历史决策，标注取代关系。
- 里程碑状态随实现推进更新；目录树仅改"现状"小节，目标树反映最终形态。
