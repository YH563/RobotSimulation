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

1. **One-way dependency**: `Host → OpenGL/Robot → Core`; no reverse references.
2. **Domains and frameworks are separate**: `Core` does not know about robots; robot semantics live only in `Robot`.
3. **Library boundary first**: the public API is constrained as if published as a library from the start.
4. **Pure functions are static; stateful systems use interfaces**: pure computation uses static methods + explicit options; replaceable/lifecycle-holding systems use interfaces and instances.
5. **Portable across UIs**: public signatures never expose Silk/GL/window types; the backend and host are replaceable.

---

## 2. Packages & Layering

```text
┌─────────────────────────────────────────────────────────────────┐
│  (sample) RobotSimulation.Host  — exe, window/input, composition│
│          references: Core, Robot, OpenGL                        │
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
| `Core` | Scene object model, pure-CPU data, render abstraction interfaces, point-cloud/model import, utils | `AssimpNet`, `Microsoft.Extensions.Logging` (`Logger`) |
| `Robot` | Robot description / URDF / headless FK / `RobotModel` tree | `Core` |
| `OpenGL` | Only render backend (device layer + renderer + GPU resources + shaders) | `Core` |

**Rules**

1. `Core` must not reference `Robot`, `OpenGL`, or any UI framework type.
2. `Robot` references only `Core` (CPU data like `MeshData` / `MaterialData` / `Scene`) and plain `System.Numerics`.
3. All `Silk.NET.*` / `StbImageSharp` appear only in `OpenGL` (and the sample host's window/input), never in public signatures.
4. `AssimpNet` is a native (non-managed) dependency of `Core`'s model import; it means the `Core` package ships native assets — to keep a "pure-managed light kernel", consider moving import into a sub-package (see section 7).

> Dependency note: `Core`'s `Logger` (`Utils`) uses the full `Microsoft.Extensions.Logging` (it calls `LoggerFactory` internally), not just `Abstractions`.

---

## 3. Package Internals

**`Core/`**
```text
Scene/       GameObject, Transform, SceneGraph, Camera, Light, primitives (GameObject subclasses)
Geometry/    Primitives(internal), Raycast/Bounds/Intersect, PointCloud2Data/PointCloudIo,
             Import/ (AssimpModelLoader, LoadOptions, LoadedModel, AssimpNative)
Rendering/   IRenderContext, IRenderer, MaterialData, TextureReference/TextureColorSpace, RenderPassKind
Utils/       GameTimer, Logger, MathUtils
```

**`Robot/`**
```text
Description/ RobotDescription, Link/Visual/Geometry/Joint pure data; GeometryDescription
Urdf/        UrdfParser(internal), IAssetResolver/FileSystemAssetResolver, UrdfLocator, UrdfParseException
State/       RobotState (headless FK)
Model/       RobotModel (GameObject root + factories/drivers)
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
| ADR-011 | Host assembles the backend via `OpenGL/Device/GraphicsFactory` (interface-based composition root) | Host holds only `IRenderContext` / `IRenderer` |
| ADR-012 | Constant screen-size axes are managed by `Axes` itself; Renderer only reads | shader does isotropic compensation |
| ADR-013 | Camera is a "display-state object" exposing math state and `Rotate`/`Pan`/`Zoom`/`Reset`, plus `ScreenToWorldRay` | picking entry point |
| ADR-014 | Point cloud uses ROS `sensor_msgs/PointCloud2` field layout | `PointCloud2Data` |
| ADR-015 | Robot subtree is locked read-only after build (`Transform`); pose changes only via `RobotModel` built-in interfaces | avoid external direct mutation of joint/sub-link poses |
| ADR-016 | `RobotState` and `RobotModel` are separate: the former is pure headless FK, the latter drives the visual tree | both share one `RobotDescription` |
| ADR-017 | The shader pipeline ships as built-in backend resources (`EmbeddedShaders`); `Core` only defines `RenderPassKind` | extend/replace shading like a game engine, without a full engine |
| ADR-018 | The composition root (host) provides `GL`; the backend delivers via interfaces; the host holds interfaces + viewport size | the `Host` is wholesale swappable (bare window / WPF / Avalonia) |

---

## 5. Runtime & Threading

```text
             host UI/update thread               host render thread
               ─────────────────           ─────────────────
update loop:   scene.Update(dt)  ──► (pure data, mutate Transform/joints)
               robot.SetJointValue(...)   │
                                          ▼
render loop:                        renderer.Render(scene)
                                    ├─ walk scene.Roots
                                    ├─ dispatch by PassKind (Model/Line/Point/Axes)
                                    ├─ data→GPU resource cache/create (Mesh/Material/Texture/Shader)
                                    └─ draw
```

- **Data can be written from any thread**: `GameObject` / `Transform` / `MeshData` / `MaterialData` are pure data, buildable on any thread, then read before rendering.
- **Only the render thread touches GL / GPU resources**: `Renderer` owns all mesh/material/texture caches and disposal; `IRenderContext.Clear` is also called on the render thread.
- **The update thread only writes CPU data** and must never call GL types. `SceneGraph.Update(deltaTime)` recurses into each `GameObject.Update`.
- **`Logger` is usable on any thread**: a process-level silent facade; the host attaches providers in the composition root via `Logger.Initialize(...)`.
- **`GameTimer`** is a background fixed-frame-rate ticker (for simulation/update), not the render loop itself.

---

## 6. Extension Points

| Extension point | Location | Purpose |
|---|---|---|
| `IRenderContext` / `IRenderer` | `Core.Rendering` | swap render backend without touching core/domain |
| `IAssetResolver` | `Robot.Urdf` | custom URDF mesh/texture reference resolution (default `FileSystemAssetResolver`) |
| `RobotModel(RobotDescription, ...)` ctor | `Robot` | any description source (SDF/custom) → robot tree |
| `GameObject.Update` virtual | `Core.Scene` | per-frame update logic (trajectory, animation) |
| `GameObject.Pickable` / pick `predicate` | `Core.Scene` | participate/filter picking |
| `EmbeddedShaders` / `ShaderProgram` | `OpenGL.Resources` | custom/replace GLSL pipeline (model/line/point-cloud/axes) |

---

## 7. Packaging & Evolution

1. **Current**: single repo + namespace partitioning; `src/{Core,Robot,OpenGL}` are the future assembly/package boundaries.
2. **After API stabilizes**: mechanically split into multiple assemblies / NuGet packages per the `core`/`robot`/`opengl` API docs; fill in `PackageId` / `Version` / `readme` metadata.
3. **Data ingestion**: first wire an external write protocol (Sink) in-process; ROS2 bridge as a separate optional project.
4. **Test hosts (three host projects)**: bare window, WPF, Avalonia — each with a `RobotViewport`-style control. They prove that the library **can render standalone** in different desktop frameworks and gather relevant tests, as verification only (not published). They share the same `Core`/`Robot`/`OpenGL` chain; the only host difference is "how to obtain `GL` and how to sync the viewport".
5. **Optional**: to keep a "pure-managed light kernel", move `AssimpNet` into a standalone import sub-package.

---

## 8. Maintenance

- Record major architecture changes as new ADR rows, keep historical decisions, and mark supersession.
- Update milestone status as the implementation advances; "current state" sections reflect only actual code.
- This file and the `core`/`robot`/`opengl` module docs are reviewed together (kept consistent across the zh-CN / en pairs).
