# RobotSimulation.OpenGL Public API (English)

> Status: reflects the current code. Namespace: `RobotSimulation.OpenGL.*` · Depends on: `RobotSimulation.Core` + `Silk.NET.OpenGL` + `StbImageSharp`.
> Companion: `../architecture/en.md`, `../core/en.md`, `../robot/en.md`.

`OpenGL` is the only rendering backend: it instantiates and draws `Core`'s pure-data scene into GPU resources on the render thread. It keeps all Silk.NET / StbImageSharp types inside the implementation — hosts only hold the `IRenderContext` / `IRenderer` interfaces.

---

## 1. Composition (`Device/`)

### `GraphicsFactory` (static)
The single assembly entry point. It assembles the whole render pipeline from a raw `GL` instance; hosts need not touch concrete backend types.

```csharp
public static class GraphicsFactory
{
    // Wrap GL as IRenderContext (device layer)
    public static IRenderContext CreateContext(GL gl);

    // Create a renderer from the device context
    public static IRenderer CreateRenderer(IRenderContext context);

    // One step: the usual composition entry, returns (Context, Renderer)
    public static (IRenderContext Context, IRenderer Renderer) Create(GL gl);
}
```

### `GraphicsContext` (`IRenderContext` impl)
Device layer: holds the `GL` instance, manages viewport, clear, and device capabilities. The ctor enables depth test and back-face culling.

- `event Action<int,int>? Resized`, `Resize(int width, int height)`, `Clear(Vector4 clearColor, bool clearDepth = true)`.
- `GraphicsDeviceInfo DeviceInfo`: device/driver strings (`Vendor` / `Renderer` / `ApiVersion` / `ShaderVersion`) read once from `GL.GetString` at construction and exposed as pure strings (no `GLEnum`/`StringName` leaks out).
- `internal GL NativeGl`: for the assembly's render layer only, not exposed outward; `GL` is allowed only inside this type.
- `internal (int Width, int Height) ViewportSize`: the last viewport rectangle the host set through `Resize`. Screen-space overlays the renderer draws (the orientation gizmo) take both their anchor and — being sized relative to the window — their size from it, instead of guessing a window size.

---

## 2. Renderer (`Rendering/`)

### `Renderer` (`IRenderer` impl)
`Render(scene)` walks the scene and instantiates/draws "data → GPU resources". It owns all mesh/material/texture caches and disposal lifecycle. The walk stops at an invisible node (`GameObject.Visible == false`): that node and its whole subtree are neither drawn nor descended into, and an invisible `Light` is left out of the light collection, so it contributes no shading.

- Dispatches by `RenderPassKind` (Model/Line/Point/Axes); `Skybox` is a scene-level future background pass, not drawn by ordinary nodes.
- A frame is drawn in two passes, ordered by depth policy. The ordinary walk draws every node depth-tested, with one exception: an axes set whose `Axes.AlwaysOnTop` is on is not drawn there at all — its whole subtree is queued instead. If anything was queued, the renderer then clears the depth buffer once and draws those sets, so a per-object frame marker stays visible inside the mesh it annotates while the markers still occlude each other correctly (a nearer set wins where two overlap). A set left at the default — a `FixedWorldLength` ruler — is ordinary geometry and stays occludable. Nothing but the corner gizmo below is drawn after that.
- After the scene is drawn, a scene that keeps `SceneGraph.ShowOrientationGizmo` on gets one more pass: the orientation gizmo — three RGB arrows plus a hub ball, drawn through the axes pass — in the bottom-right corner of the viewport, in a square viewport of its own (the depth clear is confined to that square with `glScissor`, because `glClear` ignores the viewport) and under an orthographic projection built from the scene camera's direction. That keeps its pixel size independent of camera zoom and distance, while the square itself is sized as a fixed fraction of the viewport's shorter side (`Renderer.GizmoSizeRatio`, bounded by `GizmoMinSize` / `GizmoMaxSize`), so the marker grows with the window instead of being an absolute pixel square. It is never occluded and never picked, and the widget carries no backdrop, so it annotates the picture instead of covering it. The full viewport is restored afterwards — a host may set its viewport only when its size changes.
- Light params (`MaxLights = 8`) collected per frame and written into model-shader uniform arrays. Visible lights past the cap are dropped (the uniform array is that wide) and reported with **one `Logger.Warning` per overflow** (re-armed once the scene fits again, so the next overflow is news again) — "add a light at runtime" never turns into silently missing light.
- `HighlightBlend = 0.30f`: highlight blend factor.
- `FrameStats Stats`: frame timing (`Fps` / last & smoothed frame ms / `FrameCount`), measured from the interval between `Render` calls with an exponential moving average; updated on the render thread.
- Cache sweep at the start of each frame: GPU resources are stamped with the frame that used them and an entry idle for 240 frames is released, so replacing an object's data (or dropping the object from the scene) does not keep its old GPU buffers alive until the renderer is disposed. Point clouds are pulled into sync with `PointMesh.Sync(data)` before drawing, and nothing is uploaded at all while the data's revision is unchanged.
- For safety, `Render` is called from the render thread; after `_disposed`, throws `ObjectDisposedException`. It is also the scene's **frame boundary**: it calls `SceneGraph.ApplyPendingChanges()` first, folding the `Add` / `Remove` calls (and re-parents) other threads queued into `Roots` / `Lights` before anything is walked — so a host can keep adding objects from a worker thread while it renders (visible by the next frame at the latest), and a traversal can never meet "Collection was modified". With an empty queue that call returns immediately (it does not even take the lock), so a second boundary after `Update` costs nothing. **The boundary belongs to the scene's owner thread**: the render thread has to be that thread (a host that constructs its scene inside the frame loop satisfies this by construction), or the host hands the scene over with `SceneGraph.ClaimOwnership()` before the loop starts. Otherwise `Render` throws `InvalidOperationException` — the render thread and "the thread allowed to write `Roots`" have to stay one thread, which is exactly the precondition for a traversal never seeing a modified list.

The ctor needs a `GraphicsContext` (device layer), obtained by the host via `GraphicsFactory.Create(gl)`.

---

## 3. GPU Resources (`Resources/`)

These types are backend implementation details; `public` only for same-assembly/advanced host use — normally hosts use them via `IRenderer`.

| Type | Notes |
|---|---|
| `Mesh` | GPU mesh (VAO/VBO/EBO); vertex layout defined by `VertexLayout`; `Draw()` draws as triangle list |
| `LineMesh` | GPU line buffer (`GL_LINES`), for `LineData`; optional per-vertex color |
| `PointMesh` | GPU point buffer (`GL_POINTS`), for `PointCloud2Data`; optional per-vertex color, point size is a uniform; `Sync(data)` uploads incrementally by revision (only the changed slots via `BufferSubData`, the store is rebuilt only when the capacity changes) and `Draw()` issues at most two `DrawArrays` when the ring wraps |
| `Material` | GPU material: owns shader + uploaded textures; `Apply(MaterialData)` writes appearance params |
| `Texture2D` | GPU 2D texture: from `TextureReference` (file or memory), sRGB/linear internal format per `TextureColorSpace`; can gen mipmaps |
| `ShaderProgram` | Shader program + uniform cache; `Use()` / `SetUniform(...)` (multi-overload) |
| `EmbeddedShaders` | Standard shader catalog: reads each pass's GLSL from embedded resources (Model/Line/Point/Skybox/Axes .vert/.frag) |

> Endpoint: shader sources live in this assembly's `Shaders/` directory, packed as embedded resources; `Core` only defines `RenderPassKind`; hosts don't manage shader paths.

---

## 4. Custom Shaders (core extension point: current state + direction)

`OpenGL` treats the **shader pipeline** as a first-class core — customizable like a game engine, but without the weight of one. `Core`/`Robot` are entirely unaware of GLSL; the contract is only `RenderPassKind`. This section separates "already implemented" from "planned direction".

**Already implemented**
- The standard pipeline is provided by `EmbeddedShaders` and packed with the assembly (Model / Line / Point / Skybox / Axes `.vert/.frag`); hosts don't manage shader paths.
- `Renderer` compiles all standard pass shaders at startup via `EmbeddedShaders.Get(pass)` (see its ctor; `GetPassShader(pass)` is `internal`).
- Model node drawing uses `Material.Shader` (currently created by `Renderer` from the standard `_modelShader` in `CreateMaterial`); appearance params are written into uniforms via `Material.Apply(MaterialData)`.
- Lights (`MaxLights = 8`) and highlight (`HighlightBlend`) are collected by `Renderer` and written into uniform arrays for the model shader.

**Current customization boundary (accurate)**
- `ShaderProgram`, `Material(ShaderProgram)`, and `EmbeddedShaders` are `public`, usable directly (e.g. off-screen rendering or custom device logic within the assembly / by an advanced host).
- However, `Renderer`'s **node drawing pipeline does not yet expose per-node / per-material custom-GLSL injection**: the model pass is fixed to the standard `_modelShader`, and `GameObject` has no public hook for a custom GPU material.

**Planned extension points (make shading "game-engine-like")**
1. `Renderer`'s ctor could accept `IReadOnlyDictionary<RenderPassKind, ShaderProgram>` (falling back to `EmbeddedShaders`) — replace the whole pipeline, "change style without changing the scene".
2. `GameObject` / `MaterialData` could gain an optional custom pass id or shader reference; `CreateMaterial` would stop hard-coding `_modelShader` — enabling per-object custom appearance.
3. When adding a pass, the backend supplies its own GLSL for a `RenderPassKind` and registers it in the same `_passShaders` dictionary.

> None of the above changes `Core`'s zero-awareness of GLSL (ADR-017). Once implemented, you can add/replace shaders without touching the scene object model.

---

## 5. Host composition example

```csharp
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Device;

// —— once you have a GL instance in your window/render context ——
(IRenderContext context, IRenderer renderer) = GraphicsFactory.Create(gl);

// on viewport change (driven by host events, e.g. window resize)
context.Resize(viewportWidth, viewportHeight);

// per frame: clear + draw
context.Clear(new Vector4(0.2f, 0.22f, 0.25f, 1f));
renderer.Render(scene);

// optional: read performance / device info (pure data; the host draws its own overlay)
FrameStats stats = renderer.Stats;
GraphicsDeviceInfo gpu = context.DeviceInfo;

// before exit: dispose
renderer.Dispose();
context.Dispose();
```

> The composition root (window creation, input, getting the GL instance) belongs to the host; the library only assembles a `GL`. It can be embedded in Avalonia / WPF / bare windows; the host only provides a `GL` instance and syncs viewport size to `context`. The three hosts (bare window / WPF / Avalonia) differ only in "how to get `GL` and how to sync the viewport"; the rest of the chain is identical.

---

## 6. Anti-patterns

| Anti-pattern | Reason |
|---|---|
| Letting `Core` / `Robot` reference `Silk.NET.*` or `StbImageSharp` | breaks headless / serializable / cross-backend portability |
| Calling `Render`/`Clear`/GPU resource methods on a non-render thread | GL can only be operated on the render thread |
| Propagating `GL`/`GraphicsContext` as public API outward | hosts should only hold `IRenderContext`/`IRenderer` |
| Rendering on one thread while `scene.Update` runs on another | both are the scene's frame boundaries (each calls `SceneGraph.ApplyPendingChanges()` first), so they have to be the **same** thread — the scene's owner thread; a boundary on a thread that is not the owner throws `InvalidOperationException` instead of silently moving ownership |
| Constructing the scene on thread A and running the frame loop on thread B without handing it over | the owner is the constructing thread by default, so boundaries on B throw; call `SceneGraph.ClaimOwnership()` once on B before the loop starts (ownership cannot move once a boundary has run) |
