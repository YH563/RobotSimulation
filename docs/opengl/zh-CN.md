# RobotSimulation.OpenGL 公共 API（简体中文）

> 状态：反映当前实际代码。命名空间：`RobotSimulation.OpenGL.*` · 依赖：`RobotSimulation.Core` + `Silk.NET.OpenGL` + `StbImageSharp`。
> 配套：`../architecture/zh-CN.md`、`../core/zh-CN.md`、`../robot/zh-CN.md`。

`OpenGL` 是唯一渲染后端：把 `Core` 的纯数据场景（`SceneGraph` / `GameObject` / `MeshData` / `MaterialData`）在渲染线程实例化为 GPU 资源并绘制。它把 `Silk.NET.OpenGL`/`StbImageSharp` 类型全部关在实现内部——宿主只持有 `IRenderContext` / `IRenderer` 接口（见 `Core.Rendering`）。

---

## 1. 组合根 / Composition (`Device/`)

### `GraphicsFactory`（静态）
唯一的组装入口。从一个原始 `GL` 实例组装整条渲染管线，宿主不需要接触具体后端类型。

```csharp
public static class GraphicsFactory
{
    // 把 GL 包装为 IRenderContext（设备层）
    public static IRenderContext CreateContext(GL gl);

    // 由设备上下文创建渲染器
    public static IRenderer CreateRenderer(IRenderContext context);

    // 一步到位：通常组合入口，返回 (Context, Renderer)
    public static (IRenderContext Context, IRenderer Renderer) Create(GL gl);
}
```

### `GraphicsContext`（`IRenderContext` 实现）
设备层：持有 `GL` 实例，实现视口管理、清屏与设备能力。构造函数即启用深度测试与背面剔除。

- `event Action<int,int>? Resized`、`Resize(int width, int height)`、`Clear(Vector4 clearColor, bool clearDepth = true)`。
- `GraphicsDeviceInfo DeviceInfo`：设备/驱动字符串（`Vendor` / `Renderer` / `ApiVersion` / `ShaderVersion`），构造时经 `GL.GetString` 读取一次并以纯字符串暴露（不外泄 `GLEnum`/`StringName`）。
- `internal GL NativeGl`：供同程序集渲染层使用，不向外暴露；`GL` 仅允许存在于本类型内。
- `internal (int Width, int Height) ViewportSize`：宿主经 `Resize` 设置的最后一次视口矩形。渲染器绘制的屏幕空间叠加层（朝向 gizmo）用它来定位**并**定尺寸（尺寸是视口较短边的比例），而不是去猜窗口大小。

---

## 2. 渲染器 / Renderer (`Rendering/`)

### `Renderer`（`IRenderer` 实现）
`Render(scene)` 遍历场景，把「数据 → GPU 资源」实例化并绘制。它拥有所有网格/材质/纹理缓存与释放生命周期。遍历在不可见节点处终止（`GameObject.Visible == false`）：该节点与其整棵子树既不绘制也不继续下探；不可见 `Light` 不进灯光收集，不贡献任何着色。

- 按 `RenderPassKind`（Model/Line/Point/Axes）分发；`Skybox` 为场景级未来背景通道，不由普通节点绘制。
- 一帧分两趟绘制，由**深度策略**排序。普通遍历把所有节点按深度测试画掉，只有一个例外：`Axes.AlwaysOnTop` 打开的坐标轴系**完全不在这里绘制**——它整棵子树只入队。若有轴系入队，渲染器随后清一次深度缓冲再绘制它们，于是「对象自己的坐标系」这块标记在被标注的网格内部照样可见，而各轴系之间仍互相正确遮挡（两套重叠时近的那套胜出）。保持默认的轴系（`FixedWorldLength` 的尺子）就是普通几何，照旧会被遮挡。此后除下面的角落 gizmo 之外不再绘制任何东西。
- 场景画完后，若 `SceneGraph.ShowOrientationGizmo` 打开，再画一趟朝向 gizmo（三支 RGB 箭头 + 原点小球，几何走 axes 通道，**不带背景底盘**）：它占视口右下角一个自己的方形视口（深度清理限定在 `glScissor` 框内，因为 `glClear` 不受视口影响），相机只取场景相机朝向、配正交投影 → 相机远近与缩放都不改变它的像素尺寸；而方块边长本身是按**视口较短边的固定比例**取的（`Renderer.GizmoSizeRatio`，带像素上下限 `GizmoMinSize` / `GizmoMaxSize`），所以窗口变大时标记同比例变大，而不是一个固定像素的方块；因此它永不被几何遮挡、也永不参与拾取。画完会把视口恢复为整屏——宿主可能只在尺寸变化时才设置视口。
- 灯光参数（`MaxLights = 8`）每帧收集并写入模型着色器 uniform 数组。超出上限的可见灯会被丢弃（着色器的 uniform 数组就这么宽），并在**溢出期间记一次** `Logger.Warning`（场景重新装得下之后自动复位，下次溢出会再报），所以「运行时往场景里加灯」不会变成静默少光。
- `HighlightBlend = 0.30f`：高亮混合系数。
- `FrameStats Stats`：帧时序统计（`Fps` / 最近帧与平滑帧耗时 / `FrameCount`），由两次 `Render` 调用间隔经指数滑动平均得到；在渲染线程更新。
- 每帧开头的缓存清扫：GPU 资源缓存按帧打戳，连续 240 帧未被任何节点用到的条目会被释放——这样替换某个对象的数据（或把它移出场景）不会把它旧的 GPU 缓冲一直留到渲染器销毁；点云绘制前先 `PointMesh.Sync(data)` 拉取同步，修订号未变则完全不做任何上传。
- 出于安全，`Render` 从渲染线程调用；`_disposed` 后会抛 `ObjectDisposedException`。它同时是场景的**帧边界**：先调用 `SceneGraph.ApplyPendingChanges()`，把其它线程排队的 `Add` / `Remove`（以及重挂）合入 `Roots` / `Lights` 之后才开始遍历——所以宿主可以一边渲染一边从 worker 线程往场景里加对象（最迟下一帧可见），而渲染遍历永远看不到「枚举中被改」。队列为空时这次调用立刻返回（连锁都不取），所以 `Update` 之后再来一次不会白花时间。**帧边界只属于场景属主线程**：渲染线程必须就是它（宿主在帧循环里 `new SceneGraph()` 即满足），或者由宿主在开跑前用 `SceneGraph.ClaimOwnership()` 交接过去；否则 `Render` 会抛 `InvalidOperationException`——渲染线程与「允许写 `Roots` 的线程」必须永远是同一个，这正是渲染遍历看不到列表被改的前提。

构造函数需要 `GraphicsContext`（设备层），宿主经 `GraphicsFactory.Create(gl)` 得到。

---

## 3. GPU 资源 / Resources (`Resources/`)

这些类型是后端实现细节，`public` 仅为同程序集/高级宿主可用；通常宿主无需直接使用（推荐经由 `IRenderer`）。

| 类型 | 说明 |
|---|---|
| `Mesh` | GPU 网格（VAO/VBO/EBO）；顶点布局由 `VertexLayout` 定义；`Draw()` 以三角形列表绘制 |
| `LineMesh` | GPU 线段缓冲（`GL_LINES`），对应 `LineData`；可选逐顶点颜色 |
| `PointMesh` | GPU 点缓冲（`GL_POINTS`），对应 `PointCloud2Data`；可选逐顶点颜色，点尺寸为 uniform；`Sync(data)` 按修订号增量上传（只对变更槽位 `BufferSubData`，仅在容量变化时重建缓冲），环形折返时 `Draw()` 最多两次 `DrawArrays` |
| `Material` | GPU 材质：持有着色器与已上传纹理；`Apply(MaterialData)` 写入外观参数 |
| `Texture2D` | GPU 二维纹理：从`TextureReference`（文件或内存）创建，按 `TextureColorSpace` 决定 sRGB/线性内格式；可生成 mipmap |
| `ShaderProgram` | 着色器程序与 uniform 缓存；`Use()` / `SetUniform(...)`（多样式重载） |
| `EmbeddedShaders` | 标准着色器目录：从嵌入资源读取各 pass 的 GLSL（Model/Line/Point/Skybox/Axes .vert/.frag） |

> 端点：着色器源文件位于本程序集 `Shaders/` 目录，作为嵌入式资源打包；`Core` 只约定 `RenderPassKind`，宿主无需管理着色器路径。

---

## 4. 自定义着色器 / Custom Shaders（核心扩展点：现状 + 扩展方向）

`OpenGL` 把**着色管线**当作核心来对待——像游戏引擎那样可自定义，但又避免整引擎的沉重。`Core`/`Robot` 完全不感知 GLSL，约定只在 `RenderPassKind` 上。本节如实区分「当前已实现」与「规划的扩展方向」。

**当前已实现**
- 标准管线由 `EmbeddedShaders` 提供并随程序集打包（Model / Line / Point / Skybox / Axes 的 `.vert/.frag`），宿主无需管理着色器文件路径。
- `Renderer` 在启动时从 `EmbeddedShaders.Get(pass)` 编译全部标准 pass 着色器（见其构造函数；`GetPassShader(pass)` 为 `internal`）。
- 模型节点绘制使用 `Material.Shader`（当前由 `Renderer` 以标准 `_modelShader` 创建，见 `CreateMaterial`），材质参数经 `Material.Apply(MaterialData)` 写入 uniform。
- 灯（`MaxLights = 8`）与高亮（`HighlightBlend`）由 `Renderer` 收集并写入 uniform 数组，供模型着色器消费。

**当前自定义边界（如实说明）**
- `ShaderProgram`、`Material(ShaderProgram)`、`EmbeddedShaders` 均为 `public`，可直接使用（如同程序集内/高级宿主做离屏渲染或自定义设备逻辑）。
- 但 `Renderer` 的**节点绘制管线尚未开放单节点/单材质自定义 GLSL 注入**：模型 pass 固定用标准 `_modelShader`，`GameObject` 没有可挂接自定义 GPU 材质的公共入口。

**规划的扩展点（把 shader 变成"像写游戏那样"的自定义核心）**
1. `Renderer` 构造可接受 `IReadOnlyDictionary<RenderPassKind, ShaderProgram>`（缺省用 `EmbeddedShaders` 兜底）——整体替换管线，"换风格不改场景"。
2. `GameObject` / `MaterialData` 增加可选的自定义 pass 标识或着色器引用；`CreateMaterial` 据此不再写死 `_modelShader`——支持"单物自定义外观"。
3. 新增 pass 时，后端为某 `RenderPassKind` 提供自己的 GLSL 并注册到同一 `_passShaders` 字典。

> 上述任一方案都不改变 `Core` 对 GLSL 的零感知（ADR-017）。实现后即可做到：新增/替换着色器无需改动场景对象模型。

---

## 5. 宿主接入示例 / Host composition example

```csharp
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Device;

// —— 在你的窗口/渲染上下文中拿到 GL 实例后 ——
(IRenderContext context, IRenderer renderer) = GraphicsFactory.Create(gl);

// 视口变化时（由宿主事件驱动，如窗口 Resize）
context.Resize(viewportWidth, viewportHeight);

// 每帧：清屏 + 绘制
context.Clear(new Vector4(0.2f, 0.22f, 0.25f, 1f));
renderer.Render(scene);

// 可选：读取性能/设备信息（纯数据，叠加层由宿主自绘）
FrameStats stats = renderer.Stats;
GraphicsDeviceInfo gpu = context.DeviceInfo;

// 退出前释放
renderer.Dispose();
context.Dispose();
```

> 组合根（窗口创建、输入、GL 实例的获取）属于宿主；库只负责「拿到 `GL` 后如何组装」。可嵌入 Avalonia / WPF / 裸窗口，宿主仅需提供 `GL` 实例并把视口尺寸同步到 `context`。三宿主（裸窗口 / WPF / Avalonia）的差异只在「如何拿 `GL` 与如何同步视口」，其余链路相同。

---

## 6. 反例 / Anti-patterns

| 反例 | 原因 |
|---|---|
| 让 `Core` / `Robot` 引用 `Silk.NET.*` 或 `StbImageSharp` | 破坏 headless / 可序列化 / 跨后端可移植性 |
| 在非渲染线程调用 `Render`/`Clear`/GPU 资源方法 | GL 只能在渲染线程操作 |
| 把 `GL`/`GraphicsContext` 当作 public API 向外传播 | 宿主只应持有 `IRenderContext`/`IRenderer` |
| 一个线程渲染、另一个线程跑 `scene.Update` | 两者都是场景的帧边界（都会先 `SceneGraph.ApplyPendingChanges()`），必须是**同一个**线程——即场景属主线程；帧边界在非属主线程上会抛 `InvalidOperationException`，而不是静默换属主 |
| 在 A 线程 `new SceneGraph()`、在 B 线程跑帧循环，却不交接 | 属主默认是构造线程，帧边界在 B 上会抛；在 B 上开跑前调一次 `SceneGraph.ClaimOwnership()` 即可（只此一次，跑过帧边界之后就不能再交接了） |
