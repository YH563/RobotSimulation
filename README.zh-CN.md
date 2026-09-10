# RobotSimulation

> English version: [`README.md`](README.md) / 英文版见 [`README.md`](README.md)。

一个自研、无 ROS 强依赖、可嵌入桌面 UI 的**机器人 3D 可视化与仿真库**。它把「场景对象模型、纯 CPU 渲染数据、机器人领域模型」与「具体图形后端、具体 UI 框架」彻底解耦，远期对标 rviz。同一套场景既能嵌入 Avalonia / WPF / 自绘窗口，也能脱离 UI 做 headless 计算（URDF 解析、正向运动学 FK、位姿与点云处理）。

## 1. 包含的程序集 / Packages

仓库按「未来可发布的 NuGet 包」边界组织，目录即程序集边界。三个库程序集 +（不发布的）宿主项目：

| 程序集 / Assembly | 职责 / Responsibility | 依赖 / Depends on |
|---|---|---|
| **`RobotSimulation.Core`** | 引擎内核：场景对象模型（`Scene`）、纯 CPU 几何数据与模型导入（`Geometry`）、渲染抽象接口与材质/纹理描述（`Rendering`）、点云（`PointCloud2Data`）、工具（`Utils`）。零图形、零 UI 依赖。 | `AssimpNet`、`Microsoft.Extensions.Logging` |
| **`RobotSimulation.Robot`** | 机器人领域模型：URDF / 描述（`Description`、`Urdf`）、headless 正向运动学（`State`）、机器人 `GameObject` 树构建（`RobotModel`）。 | `Core` |
| **`RobotSimulation.OpenGL`** | 唯一渲染后端：Silk.NET OpenGL + StbImageSharp 的网格 / 材质 / 纹理 / **着色器**与渲染器（`Device`、`Rendering`、`Resources`）。 | `Core` |
| *(test) `BareWindowTest`* | 裸窗口宿主测试：极简 Silk.NET 窗口，**不含任何 UI 框架**；同时充当自动化冒烟测试（`--smoke [frames]` → 退出码 0/1）。**不发布**。 | `Core`, `Robot`, `OpenGL` |
| *(test) `AvaloniaTest`* | Avalonia 宿主测试：把本库嵌入 `RobotViewportControl`（派生自 `Avalonia.OpenGL.Controls.OpenGlControlBase`），并把 GPU / 帧率 / 冒烟信息按**与裸窗口宿主完全相同的措辞**打印到控制台。**不发布**。 | `Core`, `Robot`, `OpenGL`, `Avalonia` |
| *(data) `BareWindowTest/Assets`、`AvaloniaTest/Assets`* | 各宿主测试自己的测试数据：每个工程一个 `Assets/` 目录——`Models/`（URDF + mesh，如 `fairino3_v6`）与 `PointClouds/`（.pcd / .ply），由该工程的 `.csproj` 拷到自己的可执行文件旁。宿主读取的是 `Assets/` 下的固定相对路径，因此一次运行完全由代码描述：无路径参数、无共享目录、无路径表。 | — |

> 打包元数据（`PackageId` / `Version` / `readme` 等）在正式发布前补全；当前 `src/{Core,Robot,OpenGL}` 的划分已等同于未来三个 NuGet 包。

## 2. 核心特性 / Feature Highlights

- **场景对象模型**: `GameObject` + `Transform` 层级、`SceneGraph` 根容器，`Camera`（轨道相机）、`Light`（点光/方向光）。
- **可视化原语**: `Box` / `Sphere` / `Cylinder` / `Capsule` / `GroundPlane` / `Arrow` / `Axes` / `Grid` / `Curve` / `PointCloud`。
- **纯 CPU 渲染数据**: `MeshData` / `LineData` / `PointCloud2Data` / `MaterialData` / `TextureReference`，可任意线程构建、可复用。
- **渲染抽象**: `IRenderContext` / `IRenderer`，`GraphicsFactory` 组合根；`OpenGL` 是唯一实现。
- **性能与设备信息**: `IRenderer.Stats`（`FrameStats`：FPS / 帧耗时）与 `IRenderContext.DeviceInfo`（`GraphicsDeviceInfo`：显卡厂商 / 型号 / GL & GLSL 版本），均为纯数据，叠加层由宿主自绘。
- **模型导入**: `AssimpModelLoader`（STL / OBJ / DAE / glTF 等），点云 `PointCloudIo`（PCD / PLY）。
- **机器人**: `RobotModel`（URDF → 机器人 GameObject 树、is a `GameObject`）、`RobotState`（headless FK）、纯数据描述、可扩展的 `IAssetResolver`。
- **自定义着色器**: OpenGL 后端内置完整 GLSL 管线（Model / Line / Point / Skybox / Axes）作为嵌入资源，架构上刻意让「着色器」成为可做到游戏引擎式的一等扩展点（详见 `docs/opengl/zh-CN.md`；单节点自定义着色器注入为下一步规划）。
- **拾取**: `Camera.ScreenToWorldRay` → `SceneGraph.Pick` / `PickAndHighlight`（含高亮反馈）。
- **跨 UI 嵌入**: public 签名永不出现 `Silk.NET.*` / OpenGL / 窗口类型；后端与宿主可整体替换。

## 3. 安装 / Install

```bash
# 引擎内核（场景/几何/点云/工具）
dotnet add package RobotSimulation.Core

# 机器人领域模型
dotnet add package RobotSimulation.Robot

# OpenGL 渲染后端（Silk.NET + StbImageSharp）
dotnet add package RobotSimulation.OpenGL
```

> 版本号与 `PackageId` 将在首次发布时确定；仓库当前以 `net10.0` 为目标框架。

## 4. 快速上手 / Quick Start

### 4.1 纯 headless：解析 URDF + 正向运动学（无需渲染）

```csharp
using RobotSimulation.Robot;

// URDF 文本 → 纯数据描述 → headless 关节状态与 FK
var state = RobotModel.Parse(File.ReadAllText("robot.urdf")).CreateState();

state.SetJointValue("shoulder", 1.2f);        // revolute: 弧度 (rad)
state.SetJointValue("slider", 0.35f);          // prismatic: 米 (m)

Matrix4x4 eePose = state.GetLinkGlobalPose("tool0");
```

### 4.2 可视化：搭建场景 + 加入机器人（渲染由后端处理）

```csharp
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Robot;

var scene = new SceneGraph();                       // 内置默认相机/灯光/网格/坐标轴

// 一个简单盒子图元
scene.Add(new Box(1f, 0.5f, 0.2f, name: "base"));

// 机器人：一棵 GameObject 树，作为普通可绘制节点加入
var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);
robot.SetJointValue("shoulder", 0.8f);

// 每次更新：驱动关节、更新场景（后台线程），随后由 Renderer 在渲染线程绘制
scene.Update(deltaTime);
```

### 4.3 接入图形后端（以 OpenGL 为例）

```csharp
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Device;

// 在你的窗口/渲染上下文里拿到 GL 实例后组装：
(IRenderContext context, IRenderer renderer) = GraphicsFactory.Create(gl);

context.Resize(viewportWidth, viewportHeight);      // 视口 = 像素宽/高
renderer.Render(scene);                             // 每帧调用
```

> 组合根（窗口、输入、GL 实例的创建）属于宿主；库只负责「拿到 `GL` 后如何组装为 `IRenderContext`/`IRenderer`」。

### 4.4 嵌入 UI 框架（以 Avalonia 为例）

本库从不创建窗口与 GL 上下文，所以 UI 宿主只需回答两件事：**`GL` 从哪来**、**画进哪个帧缓冲**。参考实现见 `src/AvaloniaTest/Controls/RobotViewportControl.cs`：

```csharp
public class RobotViewportControl : OpenGlControlBase          // Avalonia 按控件下发上下文
{
    protected override void OnOpenGlInit(GlInterface gl)
    {
        GL silkGl = GL.GetApi(gl.GetProcAddress);               // 把宿主的函数地址包成 Silk 门面
        (_context, _renderer) = GraphicsFactory.Create(silkGl);  // 整个组装根就这一行
        _scene = new SceneGraph();
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer); // 画进控件自己的 FBO
        _context.Resize(pixelWidth, pixelHeight);   // 视口用物理像素 = 控件尺寸 × RenderScaling
        _context.Clear(_scene.BackgroundColor);
        _renderer.Render(_scene);
        RequestNextFrameRendering();                // 维持帧循环
    }
}
```

仓库内提供两个可直接运行的宿主测试，二者**不接受任何数据参数**：各自加载写在自己源码里的那份测试数据——`Assets/Models/**` 与 `Assets/PointClouds/**`，由工程文件拷到可执行文件旁——并把相同的 GPU / FPS / 冒烟信息打印到控制台，因此两次运行可以逐行对比：

```bash
# 任一宿主：渲染 120 帧、打印 GPU + FPS、退出码 0，可直接作为 CI 冒烟测试
dotnet run --project src/BareWindowTest -- --smoke 120
dotnet run --project src/AvaloniaTest  -- --smoke 120

# 不带 --smoke 时同样的命令会打开窗口，一直运行到关窗
dotnet run --project src/AvaloniaTest
```

一次运行只装载**一个场景**，但覆盖完整数据集：URDF 内置几何（`primitives.urdf`）、社区 `urdf_tutorial` 包里的两个机器人、带 STL mesh 的 `fairino3_v6` 机械臂、`formats.urdf` 里的全部五种 mesh 格式，以及两个 RGB 点云（PLY 的 `red`/`green`/`blue` 与 PCD 的打包 `rgb`）。数据来源、如何新增或下载更多数据见 [`docs/testing/zh-CN.md`](docs/testing/zh-CN.md)。

## 5. 分模块文档 / Documentation

全部按模块拆分为中文 / 英文两版——见 [`docs/README.md`](docs/README.md)。

| 文档 / Doc | 内容 / Contents |
|---|---|
| [`docs/architecture/zh-CN.md`](docs/architecture/zh-CN.md) | 包化架构：模块边界、依赖方向、运行时/渲染线程模型、关键设计决策（ADR）、扩展点、打包路线 |
| [`docs/core/zh-CN.md`](docs/core/zh-CN.md) | `RobotSimulation.Core` 公共 API 参考（Scene / Geometry / Rendering / Utils） |
| [`docs/robot/zh-CN.md`](docs/robot/zh-CN.md) | `RobotSimulation.Robot` 公共 API 参考（RobotModel / Description / Urdf / State） |
| [`docs/opengl/zh-CN.md`](docs/opengl/zh-CN.md) | `RobotSimulation.OpenGL` 公共 API + 宿主组装 + 自定义着色管线 |
| [`docs/testing/zh-CN.md`](docs/testing/zh-CN.md) | 测试数据指南：宿主测试加载什么、文件从哪里来（含样本重新生成与可选的下载清单） |

## 6. 约定与约定俗成 / Conventions

- **坐标**: 世界右手系、**Z 向上**（Z-up），相机 up = +Z；图元回转轴沿 +Z（URDF/ROS 语义）。
- **单位**: 长度用米；角度/关节角用弧度；颜色用 `Vector4` RGBA，分量 `[0,1]`。
- **渲染数据**: `MeshData` / `MaterialData` 等是纯 CPU 数据，可在任意线程构建调整；GPU 资源由渲染后端在渲染线程实例化并缓存释放。
- **线程**: 场景修改与 `SceneGraph.Update` 在更新线程；`Render` 只在渲染线程；`Logger` 任意线程可用。
- **依赖方向**: 宿主 → `OpenGL`/`Robot` → `Core`；`Core` 永不引用 `Robot` / `OpenGL` / 任何 UI 框架。

## 7. 路线图 / Roadmap

- [ ] 发布打包（`PackageId` / 版本 / NuGet 元数据）。
- [ ] `Visualization` 显示层（rviz-like Display）与外部写入协议（Sink）。
- [ ] 三个宿主示例 / 测试项目：裸窗口、WPF、Avalonia——各一个 `RobotViewport` 式控件，既证明本库**可独立渲染显示**，也作为收编相关测试的载体。**已完成**：裸窗口（`src/BareWindowTest`）✔、Avalonia（`src/AvaloniaTest`）✔；WPF 待做。
- [ ] 单元测试：几何图元、射线拾取、URDF 解析、`RobotState` FK。
- [ ]（可选）额外的非 Silk 渲染后端。

---

## 8. 许可 / License

(TODO) 发布前在此补充许可证；默认拟以宽松许可（如 MIT）发布。
