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
- `internal GL NativeGl`：供同程序集渲染层使用，不向外暴露；`GL` 仅允许存在于本类型内。

---

## 2. 渲染器 / Renderer (`Rendering/`)

### `Renderer`（`IRenderer` 实现）
`Render(scene)` 遍历场景，把「数据 → GPU 资源」实例化并绘制。它拥有所有网格/材质/纹理缓存与释放生命周期。

- 按 `RenderPassKind`（Model/Line/Point/Axes）分发；`Skybox` 为场景级未来背景通道，不由普通节点绘制。
- 灯光参数（`MaxLights = 8`）每帧收集并写入模型着色器 uniform 数组。
- `HighlightBlend = 0.30f`：高亮混合系数。
- 出于安全，`Render` 从渲染线程调用；`_disposed` 后会抛 `ObjectDisposedException`。

构造函数需要 `GraphicsContext`（设备层），宿主经 `GraphicsFactory.Create(gl)` 得到。

---

## 3. GPU 资源 / Resources (`Resources/`)

这些类型是后端实现细节，`public` 仅为同程序集/高级宿主可用；通常宿主无需直接使用（推荐经由 `IRenderer`）。

| 类型 | 说明 |
|---|---|
| `Mesh` | GPU 网格（VAO/VBO/EBO）；顶点布局由 `VertexLayout` 定义；`Draw()` 以三角形列表绘制 |
| `LineMesh` | GPU 线段缓冲（`GL_LINES`），对应 `LineData`；可选逐顶点颜色 |
| `PointMesh` | GPU 点缓冲（`GL_POINTS`），对应 `PointCloud2Data`；可选逐顶点颜色，点尺寸为 uniform |
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
