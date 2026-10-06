# RobotSimulation

> English version: [`README.md`](README.md) / 英文版见 [`README.md`](README.md)。

一个自研、无 ROS 强依赖、可嵌入桌面 UI 的**机器人 3D 可视化与仿真库**。它把「场景对象模型、纯 CPU 渲染数据、机器人领域模型」与「具体图形后端、具体 UI 框架」彻底解耦，远期对标 rviz。同一套场景既能嵌入 Avalonia / WPF / 自绘窗口，也能脱离 UI 做 headless 计算（URDF 解析、正向运动学 FK、位姿与点云处理）。

## 1. 包含的程序集 / Packages

仓库按「未来可发布的 NuGet 包」边界组织，目录即程序集边界。三个库程序集 +（不发布的）测试工程：

| 程序集 / Assembly | 职责 / Responsibility | 依赖 / Depends on |
|---|---|---|
| **`RobotSimulation.Core`** | 引擎内核：场景对象模型（`Scene`）、纯 CPU 几何数据与模型导入（`Geometry`）、渲染抽象接口与材质/纹理描述（`Rendering`）、点云（`PointCloud2Data`）、工具（`Utils`）。零图形、零 UI 依赖。 | `Silk.NET.Assimp`、`Microsoft.Extensions.Logging` |
| **`RobotSimulation.Robot`** | 机器人领域模型：URDF / 描述（`Description`、`Urdf`）、headless 正向运动学（`State`）、机器人 `GameObject` 树构建（`RobotModel`）。 | `Core` |
| **`RobotSimulation.OpenGL`** | 唯一渲染后端：Silk.NET OpenGL + StbImageSharp 的网格 / 材质 / 纹理 / **着色器**与渲染器（`Device`、`Rendering`、`Resources`）。 | `Core` |
| *(test) `RobotSimulation.Tests`* | xunit 检查（`Tests/`）：`SceneGraphThreadingTests` 覆盖场景的线程契约（属主只声明一次、跨线程 `Add` / `Remove` 与 `Transform.Parent` 入队并在帧边界提交、队列上限与丢弃计数），另有真开窗口并绘制场景的目视检查 `BareWindowTests`（其样例场景含不同线宽的曲线）。**不发布**。 | `Core`, `Robot`, `OpenGL` |

> 打包：三个库分别打包为 `RobotSimulation.Core` / `.Robot` / `.OpenGL`。`PackageId`、`Version`、`Authors`、`RepositoryUrl`、`PackageReadmeFile` 与符号包均已配置（`Directory.Build.props` + 各 `.csproj`），且每个包都带上生成的 XML 文档文件供 IntelliSense 使用——`dotnet pack RobotSimulation.sln` 产出三个 `.nupkg` + `.snupkg`。**许可**也已确定：MIT——在 `Directory.Build.props` 中以 SPDX 表达式 `MIT` 声明，仓库根另有配套的 `LICENSE`，且每个包都会随包携带一份，因此 `dotnet pack` 的产物自带许可声明。

## 2. 核心特性 / Feature Highlights

- **场景对象模型**: `GameObject` + `Transform` 层级、`SceneGraph` 根容器，`Camera`（轨道相机）、`Light`（点光/方向光）。
- **可视化原语**: `Box` / `Sphere` / `Cylinder` / `Capsule` / `GroundPlane` / `Arrow` / `Axes` / `Grid` / `Curve` / `PointCloud`。
- **纯 CPU 渲染数据**: `MeshData` / `LineData` / `PointCloud2Data` / `MaterialData` / `TextureReference`，可任意线程构建、可复用。
- **渲染抽象**: `IRenderContext` / `IRenderer`，`GraphicsFactory` 组合根；`OpenGL` 是唯一实现。
- **性能与设备信息**: `IRenderer.Stats`（`FrameStats`：FPS / 帧耗时）与 `IRenderContext.DeviceInfo`（`GraphicsDeviceInfo`：显卡厂商 / 型号 / GL & GLSL 版本），均为纯数据，叠加层由宿主自绘。
- **模型导入**: `AssimpModelLoader`（STL / OBJ / DAE / glTF 等），点云 `PointCloudIo`（PCD / PLY）。
- **机器人**: `RobotModel`（URDF → 机器人 GameObject 树、is a `GameObject`）、`RobotState`（headless FK）、纯数据描述、可扩展的 `IAssetResolver`。
- **自定义着色器**: OpenGL 后端内置完整 GLSL 管线（Model / Line / Point / Skybox / Axes）作为嵌入资源，架构上刻意让「着色器」成为可做到游戏引擎式的一等扩展点（详见 `docs/opengl/zh-CN.md`；单节点自定义着色器注入为下一步规划）。
- **拾取 / 选择反馈**: `Camera.ScreenToWorldRay` → `SceneGraph.Pick` / `PickAndSelect`（选中即高亮 + 显示该对象自身的坐标系——该轴系带 `AlwaysOnTop`：在场景画完后、清空深度缓冲的画面之上绘制，所以它所在的那块网格再也埋不掉这个「用来解释坐标系」的标记；纯显示，库内没有拖拽手柄，反馈永远改不到机器人的关节姿态；`PickAndHighlight` 只做高亮）；另有渲染器绘制的无背景屏幕空间朝向 gizmo（`ShowOrientationGizmo`）。
- **跨 UI 嵌入**: `Core` / `Robot` 完全不接触图形与窗口 API；`OpenGL` 只在"宿主自己的 GL 句柄必须穿过边界"处出现 Silk.NET（`GraphicsFactory.Create` / `CreateContext` 及经由它上传的资源类型）——后端与宿主可整体替换。

## 3. 安装 / Install

```bash
# 引擎内核（场景/几何/点云/工具）
dotnet add package RobotSimulation.Core

# 机器人领域模型
dotnet add package RobotSimulation.Robot

# OpenGL 渲染后端（Silk.NET + StbImageSharp）
dotnet add package RobotSimulation.OpenGL
```

> 版本号与 `PackageId` 将在首次发布时确定；仓库当前以 `net8.0` 为目标框架。

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
using System.Numerics;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Robot;

var scene = new SceneGraph();                       // 内置默认相机/灯光/网格（世界坐标轴需手动开启）

// 一个简单盒子图元
scene.Add(new Box(1f, 0.5f, 0.2f, name: "base"));

// 机器人：一棵 GameObject 树，作为普通可绘制节点加入
var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);
robot.SetJointValue("shoulder", 0.8f);

// 每帧逻辑是「注入」的，而非继承子类（可选）：任意节点都能挂行为
var marker = new Box(0.2f, 0.2f, 0.2f, name: "marker");
scene.Add(marker);
float spin = 0;
marker.AddUpdate((go, dt) =>
    go.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin += (float)dt));

// 每帧：驱动关节、跑行为/更新场景，随后绘制——更新与渲染共用同一个帧循环线程；
// SceneGraph.Add/Remove 还可以从任意其它线程调用（会先入队，到该帧边界才生效）
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

本库从不创建窗口与 GL 上下文，所以 UI 宿主只需回答两件事：**`GL` 从哪来**、**画进哪个帧缓冲**。一个 Avalonia 嵌入大致长这样：

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

本库不附带可直接运行的宿主程序。目视检查放在测试工程里：`Tests/BareWindowTests.cs` 打开一个 Silk.NET 窗口、渲染一个默认 `SceneGraph`（其样例场景含不同线宽的曲线），窗口关闭即返回；它需要显示设备，因此 CI 的测试步骤会把它过滤掉。

## 5. 分模块文档 / Documentation

全部拆分为中文 / 英文两版，模块如此，本索引亦然——[`docs/README.zh-CN.md`](docs/README.zh-CN.md)（中文）/ [`docs/README.md`](docs/README.md)（English）。

| 文档 / Doc | 内容 / Contents |
|---|---|
| [`docs/architecture/zh-CN.md`](docs/architecture/zh-CN.md) | 包化架构：模块边界、依赖方向、运行时/渲染线程模型、关键设计决策（ADR）、扩展点、打包路线 |
| [`docs/core/zh-CN.md`](docs/core/zh-CN.md) | `RobotSimulation.Core` 公共 API 参考（Scene / Geometry / Rendering / Utils） |
| [`docs/robot/zh-CN.md`](docs/robot/zh-CN.md) | `RobotSimulation.Robot` 公共 API 参考（RobotModel / Description / Urdf / State） |
| [`docs/opengl/zh-CN.md`](docs/opengl/zh-CN.md) | `RobotSimulation.OpenGL` 公共 API + 宿主组装 + 自定义着色管线 |

## 6. 约定与约定俗成 / Conventions

- **坐标**: 世界右手系、**Z 向上**（Z-up），相机 up = +Z；图元回转轴沿 +Z（URDF/ROS 语义）。
- **单位**: 长度用米；角度/关节角用弧度；颜色用 `Vector4` RGBA，分量 `[0,1]`。
- **渲染数据**: `MeshData` / `MaterialData` 等是纯 CPU 数据，可在任意线程构建调整；GPU 资源由渲染后端在渲染线程实例化并缓存释放。
- **线程**: `Update` 与 `Render` 共用同一个帧循环线程（二者都是场景的帧边界）；`SceneGraph.Add` / `Remove` 可以从**任意**线程调用——非属主线程上先入队、到下一个帧边界才生效，所以正在渲染的场景也能持续生长；`Logger` 任意线程可用。
- **依赖方向**: 宿主 → `OpenGL`/`Robot` → `Core`；`Core` 永不引用 `Robot` / `OpenGL` / 任何 UI 框架。

## 7. 路线图 / Roadmap

- [x] 打包元数据（`PackageId` / 版本 / NuGet 元数据 / README / 许可）：由 `Directory.Build.props` 加各 `.csproj` 承担，已用 `dotnet pack` 验证——产出 3 个 `.nupkg` + 3 个 `.snupkg`，每个包都带 XML 文档、README 与一份 `LICENSE`。推送至源仍是单独的手动步骤。
- [ ] `Visualization` 显示层（rviz-like Display）与外部写入协议（Sink）。
- [ ] 三个宿主示例 / 测试项目：裸窗口、WPF、Avalonia——各一个 `RobotViewport` 式控件，既证明本库**可独立渲染显示**，也作为收编相关测试的载体。**已放弃**：独立宿主程序已移除，仓库只保留 `RobotSimulation.Tests`（xunit，含真开窗口的目视检查 `BareWindowTests`），宿主嵌入交由使用方自行实现。
- [ ] 单元测试：几何图元、射线拾取、URDF 解析、`RobotState` FK。**进行中**：`Tests/`（xunit）现有 `SceneGraphThreadingTests`——场景线程契约的 **10 项**无头检查（属主只声明一次、跨线程 `Add` / `Remove` 与 `Transform.Parent` 入队并在帧边界提交、队列上限与丢弃计数、根节点随父节点迁出 `Roots`）✔，以及 `BareWindowTests` 这个真开窗口的目视 / 手工检查 ✔。早先那 37 项用例（URDF 解析与 `IAssetResolver` 路径解析、几何图元、射线拾取、默认 `SceneGraph` 的选择契约）随旧 `src/RobotSimulation.Tests/` 目录一并删除；形状 / 拾取 / 路径解析与 `RobotState` FK 待补。
- [ ]（可选）额外的非 Silk 渲染后端。

---

## 8. 许可 / License

MIT——见 [`LICENSE`](LICENSE)。三个库包在 `Directory.Build.props` 中以 SPDX 表达式 `MIT` 声明，因此
`dotnet pack` 的产物自带许可声明、无需包内许可文件；不过每个包仍会随 README 一起打进一份 `LICENSE`。
