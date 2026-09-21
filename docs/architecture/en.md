# RobotSimulation Architecture (English)

> Status: v0.1 reflecting the current implementation. Companion: `../../README.md`, `../core/en.md`, `../robot/en.md`, `../opengl/en.md`.

This document describes the architecture from the perspective of *publishable NuGet libraries*: package boundaries, dependency direction, the runtime/render threading model, key design decisions (ADRs), and extension points.

---

## 1. Goals & Principles

**Goals**

- Build geometry and robot scenes from URDF parsing and model loading.
- Provide visualization primitives (curves, point clouds, axes, primitives).
- Decouple the render core from any UI framework so it can be embedded in Avalonia / WPF / bare windows.
- `Core` / `Robot` depend on no Silk.NET / OpenGL types; `OpenGL` is the only backend.
- The same data works both headless and for rendering.

**Principles (by priority)**

1. **One-way dependency**: host → `OpenGL`/`Robot` → `Core`; no reverse references.
2. **Domains and frameworks are separate**: `Core` does not know about robots; robot semantics live only in `Robot`.
3. **Library boundary first**: the public API is constrained as if published as a library from the start.
4. **Pure functions are static; stateful systems use interfaces**: pure computation uses static methods + explicit options; replaceable/lifecycle-holding systems use interfaces and instances.
5. **Portable across UIs**: public signatures never expose Silk/GL/window types; the backend and host are replaceable.

---

## 2. Packages & Layering

```text
┌─────────────────────────────────────────────────────────────────┐
│  (test) BareWindowTest / AvaloniaTest — exe, window/input       │
│          references: Core, Robot, OpenGL (+Avalonia)            │
└───────────────┬─────────────────────────────────────────────────┘
                │
   ┌────────────┴────────────┐
   │                         │
   ▼                         ▼
┌──────────────────┐   ┌──────────────────┐
│  RobotSimulation │   │  RobotSimulation │
│  .Robot          │   │  .OpenGL         │
│  (URDF/desc/state│   │  (Silk.NET)      │
│  refs: Core      │   │  refs: Core      │
└────────┬─────────┘   └────────┬─────────┘
         │                      │
         └──────────┬───────────┘
                    ▼
        ┌──────────────────────────┐
        │  RobotSimulation.Core    │
        │  kernel: Scene/Geometry  │
        │  /Rendering ifaces/Utils │
        │  (no GL, no UI)          │
        └──────────────────────────┘
```

| Package | Public responsibility | Depends on |
|---|---|---|
| `Core` | Scene object model, pure-CPU data, render abstraction interfaces, point-cloud/model import, utils | `Silk.NET.Assimp`, `Microsoft.Extensions.Logging` (`Logger`) |
| `Robot` | Robot description / URDF / headless FK / `RobotModel` tree | `Core` |
| `OpenGL` | Only render backend (device layer + renderer + GPU resources + shaders) | `Core` |

**Rules**

1. `Core` must not reference `Robot`, `OpenGL`, or any UI framework type.
2. `Robot` references only `Core` (CPU data like `MeshData` / `MaterialData` / `Scene`) and plain `System.Numerics`.
3. All `Silk.NET.*` / `StbImageSharp` appear only in `OpenGL` (and the host tests' window/input), never in public signatures.
4. `Silk.NET.Assimp` is a native (non-managed) dependency of `Core`'s model import; it means the `Core` package ships native assets (distributed per RID, with nothing for a host to bootstrap) — to keep a "pure-managed light kernel", consider moving import into a sub-package (see section 7).

> Dependency note: `Core`'s `Logger` (`Utils`) uses the full `Microsoft.Extensions.Logging` (it calls `LoggerFactory` internally), not just `Abstractions`.

---

## 3. Package Internals

**`Core/`**
```text
Scene/          framework kernel: GameObject, Transform, SceneGraph, Camera, Light, RaycastHit
  Primitives/     ready-made nodes: Box/Sphere/Cylinder/Capsule/GroundPlane/Arrow/Axes/Grid/Curve/PointCloud
  Behaviors/      per-frame logic injection: IUpdateBehavior, DelegateUpdateBehavior
Geometry/       pure CPU geometry: MeshData/LineData/VertexLayout, Primitives(internal), Ray/Raycast/Bounds
  PointCloud/     point cloud: PointCloud2Data, PointField, PointFieldDataType, PointCloudIo
  Import/         Assimp import: AssimpModelLoader, LoadOptions, LoadedModel
Rendering/      render abstraction: IRenderContext, IRenderer, MaterialData, TextureReference(+TextureColorSpace),
                RenderPassKind, FrameStats, GraphicsDeviceInfo
Utils/          GameTimer, Logger, MathUtils
```

> Folders and namespaces are **deliberately decoupled** (ADR-020): a folder may be finer than its namespace, but
> `namespace` is **never** changed because of a folder move. For example `Scene/Primitives/Box.cs` still lives in
> `RobotSimulation.Core.Scene`. Reorganizing folders has zero impact on consumers; the root `.editorconfig` locks
> this in with `dotnet_style_namespace_match_folder = false`.

**`Robot/`**
```text
Description/ RobotDescription, Link/Visual/Geometry/Joint pure data; GeometryDescription
Urdf/        UrdfParser(internal), IAssetResolver/FileSystemAssetResolver, UrdfParseException
State/       RobotState (headless FK)
(root)       RobotModel (GameObject root + factories/drivers)
```

**`OpenGL/`**
```text
Device/      GraphicsFactory (static composition root), GraphicsContext (IRenderContext)
Rendering/   Renderer (IRenderer), RenderPassKind dispatch, lighting/highlight management
Resources/   Mesh, LineMesh, PointMesh, Material, Texture2D, ShaderProgram, EmbeddedShaders
Shaders/     Model/Line/Point/Skybox/Axes .vert/.frag (packed as embedded resources)
```

---

## 4. Key Architectural Decisions (ADR)

| # | Decision | Notes |
|---|---|---|
| ADR-001 | World coordinate system **Z-up** (right-handed), camera up = +Z | Consistent with URDF/ROS/rviz |
| ADR-002 | Primitive rotation axes along +Z (URDF/ROS semantics) | `Cylinder`/`Capsule`/`Sphere`/`Cone` |
| ADR-003 | `Core/Geometry` uses pure-CPU `MeshData`, decoupled from GL | `MeshData → Mesh` uploaded only on the render thread |
| ADR-004 | Primitive mesh generation converges in `internal Primitives` (non-public) | No mutable global state exposed |
| ADR-005 | `Robot` domain module is independent of `Core` (`Core` never references domain types) | Domain/framework boundary |
| ADR-006 | `Robot/Description` + `Robot/Urdf` are a zero-render pure-data subset | cut line for headless / physics / standalone library |
| ADR-007 | Render-backend abstraction: `Core.Rendering` defines only interfaces (`IRenderContext` / `IRenderer`); Silk/GL implemented in `OpenGL` | swap backends without touching the core |
| ADR-008 | Scene is pure data; GPU resource lifecycle belongs to `Renderer` (incl. caching & disposal) | zero render deps in domain |
| ADR-009 | Basic primitives exposed as GameObject subclasses (`Box`/`Sphere`/`Cylinder`/`Capsule`/`GroundPlane`/`Arrow`/`Axes`) | `new Box(...)` then `scene.Add` |
| ADR-010 | Input events are translated into camera commands only at the host; `Core` knows no concrete input framework | camera gesture policy belongs to the host |
| ADR-011 | The host assembles the backend via `OpenGL/Device/GraphicsFactory` (interface-based composition root) | the host holds only `IRenderContext` / `IRenderer` |
| ADR-012 | Axes sizing is managed by `Axes` itself and only read by the Renderer: constant screen size, or fixed world length | shader does isotropic compensation; a fixed length is expressed as ratio=0 with min=max=length |
| ADR-013 | Camera is a "display-state object" exposing math state and `Rotate`/`Pan`/`Zoom`/`Reset`, plus `ScreenToWorldRay` | picking entry point |
| ADR-014 | Point cloud uses ROS `sensor_msgs/PointCloud2` field layout | `PointCloud2Data` |
| ADR-015 | Robot subtree is locked read-only after build (`Transform`); pose changes only via `RobotModel` built-in interfaces | avoid external direct mutation of joint/sub-link poses |
| ADR-016 | `RobotState` and `RobotModel` are separate: the former is pure headless FK, the latter drives the visual tree | both share one `RobotDescription` |
| ADR-017 | The shader pipeline ships as built-in backend resources (`EmbeddedShaders`); `Core` only defines `RenderPassKind` | extend/replace shading like a game engine, without a full engine |
| ADR-018 | The composition root (host) provides `GL`; the backend delivers via interfaces; the host holds interfaces + viewport size | the host is wholesale swappable (bare window / WPF / Avalonia) |
| ADR-019 | Per-frame logic on `GameObject` switched to **composition injection**: a node carries an `IUpdateBehavior` list and `GameObject.Update` becomes a non-virtual dispatcher | logic can be added/removed, paused, and unit-tested at runtime; `sealed` and parsed nodes extend without a subclass |
| ADR-020 | Folders and namespaces are deliberately decoupled: a folder may be finer than its namespace, and reorganizing folders **never** changes `namespace` (locked by `.editorconfig` disabling IDE0130) | protects the public API contract; folders are internal organization only |
| ADR-021 | URDF asset lookup became a **root fallback chain**: explicit `assetDirectory` first, the URDF's sibling directory second; a `package://` **package name is always discarded** | replaces heuristic guessing with an explicit asset root (`assetDirectory` = MuJoCo's `meshdir`); the standard ROS `urdf/`+`meshes/` layout now loads without editing the URDF |
| ADR-022 | Incremental point clouds: a growable store with a ring start plus a revision / change window in the data layer, pull-style partial uploads in the backend (`PointMesh.Sync`), and frame-stamped GPU cache collection | appending moves no data and eviction only moves the ring start, so a frame uploads just the new points — and nothing at all while `Revision` is unchanged |
| ADR-023 | Orientation feedback is split by kind: a scene axes set is a plain, occludable ruler (`AxesSizing.FixedWorldLength`, `FitWorldAxesToContent` sizes it past the model) while the renderer draws a screen-space gizmo in the viewport's bottom-right corner (own viewport + scissored depth clear, never occluded, never picked); the default scene ships with the world axes off and the gizmo on | "which way is X/Y/Z" is a UI question, "how big is a metre" is a scene question — answering each in its own space keeps one from ruining the other |
| ADR-024 | Selection feedback is a pair, and the scene assembles it: `SceneGraph.Select` highlights the picked object *and* mounts that node's own local axes on it (non-pickable; `ShowSelectionAxes` on by default), while the renderer's corner gizmo stays a bare marker — three arrows and a hub ball, no backdrop. Both are **display only**: the library ships no drag affordance and the scene never writes back to a transform. The marker is drawn **on top of the geometry** (`Axes.AlwaysOnTop`): it is queued during the ordinary walk and drawn after one depth clear, so the mesh it annotates can no longer bury the very frame it is there to explain — while a world-scale ruler keeps the default and stays occludable | a highlight says *what* was picked but not which way that node's frame points, and axes at the world origin say nothing about a pick — both halves belong to the same click; and because a link's pose belongs to its joint chain, feedback that can only *show* a frame can never fight the kinematics |
| ADR-026 | Depth policy belongs to the axes set, not to the scene: `Axes.AlwaysOnTop` (default false) makes the renderer queue that set's whole subtree during the ordinary walk and draw it last, on a depth buffer it has just cleared (one `glClear(GL_DEPTH_BUFFER_BIT)` per frame, skipped entirely when nothing was queued), while the corner gizmo keeps its own viewport and scissored clear. `GameObject.ShowLocalAxes` — and therefore `SceneGraph.Select` — creates its marker with the flag on; a `FixedWorldLength` ruler leaves it off | a per-object frame marker lives *inside* the mesh it annotates, so depth-testing it buries the very frame it exists to explain; but "the annotation always wins" cannot be a scene-wide rule either, because a ruler that shows through what it measures lies about the scene. Keeping the switch on the set (the same place its sizing policy lives — ADR-012) keeps the choice where the knowledge is: the renderer only reads it, and both kinds of axes set can coexist in one scene |
| ADR-025 | Scene structure changes commit at a **frame boundary**: `SceneGraph.Add` / `Remove` still apply immediately on the owner thread, while any other thread only enqueues into a `ConcurrentQueue`; the boundary `ApplyPendingChanges()` (called first by `Update` and by every `IRenderer.Render` implementation) folds the queue into `Roots` / `Lights` and publishes the monotonic `Version` plus `PendingChangeCount` (structural changes only: poses / joint values are still publish-then-read); `Remove` also clears a selection that pointed into the removed node, and structural changes after `Dispose` are ignored (with one warning); **write access is declared once** — the constructing thread is the owner, a host whose frame loop lives on another thread hands the scene over explicitly with `ClaimOwnership()` before the loop starts, and `ApplyPendingChanges()` no longer makes the caller the owner (on a thread that is not the owner it throws `InvalidOperationException`, because the frame boundary and "the thread allowed to walk `Roots`" have to stay one thread); the `Transform.Parent` back door joins the same queue (immediate on the owner thread and it keeps `Roots` membership in step, queued on any other thread) while `Transform.Children` becomes a read-only view; and the queue gains a `MaxPendingChanges` cap with `DroppedPendingChangeCount` (over-limit changes are dropped with one warning, so misuse is visible) | "adding objects while it renders" only ever *happened* to work (live traversal + lazy GPU upload) without being a contract: a cross-thread `Add` threw `Collection was modified` out of the render traversal, removing the selected node left a dangling selection, and lights past the shader cap were dropped silently. Gathering structural changes at a frame boundary writes the publish-then-hand-off rule into the code — producers only enqueue, rendering only walks a self-consistent snapshot — at the sole cost of "visible by the next frame at the latest", which is the same semantics as Unreal/Godot/Unity command queues, PhysX/Bevy double-buffered Extract, and rviz's `queueRender()`. The second half of ADR-025 came with the implementation: letting producers only enqueue settles "a producer cannot touch the lists" but not "who counts as a producer" — the old boundary made whoever called it the owner, so a single call from another thread flipped ownership and turned the previous owner (quite possibly mid-traversal) into an outsider, which made "who may write" a per-call answer. There is one answer per frame: the owner is fixed at construction and can be handed over explicitly, once, before the loop starts, and anything outside that throws instead of silently rerouting. The `Transform.Parent` setter broke the same rule through a back door (and left a node in both `Roots` and its parent, so `Remove` had to be called twice to be rid of it), so it joins the queue too; and the uncapped queue, which let "enqueue structure at data rate" go unnoticed, gained a cap and a drop counter |

---

## 5. Runtime & Threading

```text
    producer threads (sensors / workers, any)   host frame-loop thread (update + render, one thread = scene owner)
   ─────────────────────────────────────────   ──────────────────────────────────────────────────
producers: scene.Add / scene.Remove            update:  scene.Update(dt)
           transform.Parent = parent
            ├─ owner thread? → edit lists                  ├─ frame boundary: ApplyPendingChanges()
            └─ otherwise → enqueue _pending                └─ recurse GameObject.Update (pure data)
                                                                  │
                                                     render:  renderer.Render(scene)
                                                                  ├─ frame boundary: ApplyPendingChanges()
                                                                  ├─ walk scene.Roots
                                                                  ├─ dispatch by PassKind (Model/Line/Point/Axes)
                                                                  ├─ data→GPU resource cache/create (Mesh/Material/Texture/Shader)
                                                                  ├─ draw depth-tested (an AlwaysOnTop axes set is queued here, not drawn)
                                                                  ├─ overlay pass: clear the depth buffer → draw those sets (nothing can hide them)
                                                                  └─ corner orientation gizmo (own viewport + scissored depth clear)
```

- **Data can be written from any thread**: `GameObject` / `Transform` / `MeshData` / `MaterialData` are pure data, buildable on any thread, then read before rendering (**structure** is not on that list — see the next bullet).
- **Structural changes commit at a frame boundary, and write access is declared once**: `SceneGraph.Add` / `Remove` / `Transform.Parent` (re-parenting) can be called from any thread. They take effect immediately on the owner thread — the owner being the **constructing thread**, or the frame-loop thread a host handed the scene over to with `SceneGraph.ClaimOwnership()` before the loop started; on any other thread they only enter a `ConcurrentQueue`, which the **frame boundary** `SceneGraph.ApplyPendingChanges()` folds into `Roots` / `Lights` before anything is walked. The library steps on that boundary itself — the renderer calls it at the start of every `Render`, `Update` calls it before it recurses — so a host frame loop needs no change at all (the frame's second call finds the queue empty and does not even take the lock). The boundary does **not** make the calling thread the owner: on a thread that is not the owner it throws `InvalidOperationException` and points at `ClaimOwnership()`, because "the thread allowed to walk `Roots`" and "the thread allowed to write the lists" have to stay one thread — a silent reroute turns the previous owner (quite possibly mid-traversal) into an outsider, which is how the very same framework ends up safe most of the time and throwing "Collection was modified" occasionally. Producers never touch the lists, so a traversal can never meet "Collection was modified"; `Version` climbs monotonically for hosts invalidating caches and `PendingChangeCount` is there for diagnostics, while a queue past `MaxPendingChanges` (65536 by default) drops further changes with one warning and counts them in `DroppedPendingChangeCount` (the cap is not a performance knob — it makes "enqueue structure at data rate" visible). The price is that a structural change becomes visible **at the latest on the next frame** — rendering always works off a self-consistent snapshot, which is exactly what publish-then-hand-off means.
- **`Roots` holds exactly "added *and* parentless"**: `Roots` / `Lights` contain real scene roots only; a root that later gains a parent (`Transform.Parent`) leaves `Roots` (it is reached through that parent now), and `Remove` clears both places. So "in `Roots` and under a parent at the same time" cannot happen — that state traverses, draws, picks and measures one tree twice, and takes two `Remove` calls to be rid of.
- **Child lists are read-only**: `Transform.Children` is a read-only view and re-parenting goes through a `Parent` assignment only (which is the queue above once the node is in a scene). The renderer walks those lists every frame, so making them unwritable from another thread is the other half of "a traversal can never meet a modified list".
- **Pure data carries no synchronisation of its own**: nothing locks or fences `Transform` / joint-value writes against the renderer's reads (which is what engines do too — per-frame pose writes belong to the frame-loop thread). To "compute on another thread, show it this frame", use the publish-then-read hand-off, or a data layer with a `Revision` handshake such as `PointCloud2Data`. The one thing the library does make thread-safe is **structural changes** (the bullet above). Nor should one node (or the subtree already attached to it) be modified from two threads at once: structural routing works out "does this node belong to a scene right now", and a subtree on its way into a scene is not published yet — build the subtree first and then `Add` it, or only touch it from the owner thread.
- **Only the render thread touches GL / GPU resources**: `Renderer` owns all mesh/material/texture caches and disposal; `IRenderContext.Clear` is also called on the render thread.
- **The update thread only writes CPU data** and must never call GL types. `SceneGraph.Update(deltaTime)` recurses into each `GameObject.Update`. Because `Update` and `Render` are both frame boundaries (both fold in queued structural changes), both belong to the **same thread** (the host's frame loop) — which is the scene's owner thread.
- **`Logger` is usable on any thread**: a process-level silent facade; the host attaches providers in the composition root via `Logger.Initialize(...)`.
- **`GameTimer`** is a background fixed-frame-rate ticker (for simulation/update), not the render loop itself.
- **`FrameStats` / `GraphicsDeviceInfo` are pure data**: the backend updates `IRenderer.Stats` on the render thread and fills `IRenderContext.DeviceInfo` once when the device context is created; a host reads them (on whatever thread it chooses) only to display.

---

## 6. Extension Points

| Extension point | Location | Purpose |
|---|---|---|
| `IRenderContext` / `IRenderer` | `Core.Rendering` | swap render backend without touching core/domain |
| `IAssetResolver` | `Robot.Urdf` | custom URDF mesh/texture reference resolution (default `FileSystemAssetResolver`) |
| `RobotModel(RobotDescription, ...)` ctor | `Robot` | any description source (SDF/custom) → robot tree |
| `IUpdateBehavior` / `GameObject.AddUpdate` | `Core.Scene` | inject per-frame logic (trajectory, animation) without a subclass; class-based logic via `AddBehavior<T>` |
| `GameObject.Pickable` / pick `predicate` | `Core.Scene` | participate/filter picking |
| `EmbeddedShaders` / `ShaderProgram` | `OpenGL.Resources` | custom/replace GLSL pipeline (model/line/point-cloud/axes) |

---

## 7. Packaging & Evolution

1. **Current**: single repo + namespace partitioning; `src/{Core,Robot,OpenGL}` are the future assembly/package boundaries.
2. **After API stabilizes**: mechanically split into multiple assemblies / NuGet packages per the `core`/`robot`/`opengl` API docs; fill in `PackageId` / `Version` / `readme` metadata. **Already in place**: all three library projects (`Core` / `OpenGL` / `Robot`) set `<GenerateDocumentationFile>`, so `CS1591` (missing XML comment on a public member) is reported by every build and is kept at zero, and `RobotSimulation.*.xml` is emitted alongside each assembly — a package therefore carries its IntelliSense docs automatically.
3. **Data ingestion**: first wire an external write protocol (Sink) in-process; ROS2 bridge as a separate optional project.
4. **Test hosts (three host projects)**: bare window, WPF, Avalonia — each with a `RobotViewport`-style control. They prove that the library **can render standalone** in different desktop frameworks and gather relevant tests, as verification only (not published). They share the same `Core`/`Robot`/`OpenGL` chain; the only host difference is "how to obtain `GL` and how to sync the viewport". **Done so far**: `src/BareWindowTest` (no UI framework at all; also the automated smoke test, `--smoke [frames]` → exit code 0/1) and `src/AvaloniaTest` (`RobotViewportControl : OpenGlControlBase`, obtaining `GL` through `GL.GetApi(gl.GetProcAddress)` and drawing into Avalonia's per-control framebuffer); WPF is still pending. Both take the same CLI shape — `--smoke [frames]`, and nothing else. The scene content is deliberately minimal: one URDF robot (`fairino3_v6`), the camera left exactly as `SceneGraph`'s constructor poses it, and a window that offers nothing but orbit and pick-to-highlight — so they are both an end-to-end check and a minimal embedding example to copy. The loaded file name sits in a single constant in each host's source, under that project's own `Assets/` tree, which each project file copies next to its executable. Their console output is therefore line-by-line comparable, which turns the two hosts into a cross-check of each other instead of two separate demos; for swapping in another model (the `Assets/` trees also ship built-in geometry, a community package, all five mesh formats and point-cloud samples) see `docs/testing`.
5. **Optional**: to keep a "pure-managed light kernel", move `Silk.NET.Assimp` into a standalone import sub-package.

---

## 8. Maintenance

- Record major architecture changes as new ADR rows, keep historical decisions, and mark supersession.
- Update milestone status as the implementation advances; "current state" sections reflect only actual code.
- This file and the `core`/`robot`/`opengl` module docs are reviewed together (kept consistent across the zh-CN / en pairs).
