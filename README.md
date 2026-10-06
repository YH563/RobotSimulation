# RobotSimulation

> 简体中文版本见 [`README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/README.zh-CN.md) / For the Chinese version, see [`README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/README.zh-CN.md).

A self-contained, ROS-free, embeddable **robot 3D-visualization and simulation library for desktop UIs**. It cleanly decouples the *scene object model*, *pure-CPU render data*, and *robot domain model* from any *concrete graphics backend* and *UI framework*, ultimately targeting rviz-like capabilities. The same scene embeds in Avalonia / WPF / a custom-drawn window and runs headless (URDF parsing, forward kinematics, pose & point-cloud processing).

## 1. Packages

The repo is organized by the boundaries of *future publishable NuGet packages* — a directory is an assembly boundary. Three library assemblies plus the (unpublished) test project:

| Assembly | Responsibility | Depends on |
|---|---|---|
| **`RobotSimulation.Core`** | Engine kernel: scene object model (`Scene`), pure-CPU geometry & model import (`Geometry`), render abstraction + material/texture description (`Rendering`), point cloud (`PointCloud2Data`), utils (`Utils`). Zero graphics, zero UI. | `Silk.NET.Assimp`, `Microsoft.Extensions.Logging` |
| **`RobotSimulation.Robot`** | Robot domain model: URDF / description (`Description`, `Urdf`), headless forward kinematics (`State`), robot `GameObject` tree (`RobotModel`). | `Core` |
| **`RobotSimulation.OpenGL`** | The only render backend: Silk.NET OpenGL + StbImageSharp meshes / materials / textures / shaders & renderer (`Device`, `Rendering`, `Resources`). | `Core` |
| *(test) `RobotSimulation.Tests`* | xunit checks (`Tests/`): `SceneGraphThreadingTests` covers the scene's threading contract (the owner is claimed once, cross-thread `Add` / `Remove` and `Transform.Parent` queue up and commit at a frame boundary, the queue cap and its drop counter), plus `BareWindowTests`, a visual check that really opens a window and draws the scene (its sample scene includes curves with different line widths). **Not published**. | `Core`, `Robot`, `OpenGL` |

> Packaging: the three libraries pack as `RobotSimulation.Core` / `.Robot` / `.OpenGL`. `PackageId`, `Version`, `Authors`, `RepositoryUrl`, `PackageReadmeFile` and symbol packages are configured (`Directory.Build.props` plus each `.csproj`), and every package carries its generated XML documentation file for IntelliSense — `dotnet pack RobotSimulation.sln` produces the three `.nupkg` + `.snupkg`. The **license** is settled too: MIT, declared as the SPDX expression `MIT` in `Directory.Build.props` with a matching `LICENSE` at the repo root that every package also carries — so `dotnet pack` output states its terms on its own.

## 2. Feature Highlights

- **Scene object model**: `GameObject` + `Transform` hierarchy, `SceneGraph` root container, `Camera` (orbit), `Light` (point / directional).
- **Visualization primitives**: `Box` / `Sphere` / `Cylinder` / `Capsule` / `GroundPlane` / `Arrow` / `Axes` / `Grid` / `Curve` / `PointCloud`.
- **Pure-CPU render data**: `MeshData` / `LineData` / `PointCloud2Data` / `MaterialData` / `TextureReference`, thread-friendly and reusable.
- **Render abstraction**: `IRenderContext` / `IRenderer`, `GraphicsFactory` composition root; `OpenGL` is the only implementation.
- **Performance & device info**: `IRenderer.Stats` (`FrameStats`: FPS / frame time) and `IRenderContext.DeviceInfo` (`GraphicsDeviceInfo`: GPU vendor / renderer / GL & GLSL version) — pure data the host renders as its own overlay.
- **Model import**: `AssimpModelLoader` (STL / OBJ / DAE / glTF), point clouds `PointCloudIo` (PCD / PLY).
- **Robotics**: `RobotModel` (URDF → robot `GameObject` tree, itself a `GameObject`), `RobotState` (headless FK), pure-data descriptions, extensible `IAssetResolver`.
- **Custom shaders**: the OpenGL backend ships a built-in GLSL pipeline (Model / Line / Point / Skybox / Axes) as embedded resources, and the architecture is deliberately shaped so shading can become a first-class, game-engine-like extension point (see `docs/opengl/en.md`; per-node custom-shader injection is a planned next step).
- **Picking & selection feedback**: `Camera.ScreenToWorldRay` → `SceneGraph.Pick` / `PickAndSelect` (a pick highlights the object *and* mounts that object's own local axes as a read-only marker whose set is `AlwaysOnTop` — drawn after the scene, on a cleared depth buffer, so the mesh it annotates can no longer bury the frame it is there to explain; `PickAndHighlight` flips the highlight alone) — plus the renderer's backdrop-free screen-space orientation gizmo. No drag handles exist anywhere in the library, so feedback can never edit a model's poses.
- **Cross-UI embedding**: `Core` / `Robot` touch no graphics or window API at all, and `OpenGL` mentions Silk.NET only where the host's own GL handle must cross the boundary (`GraphicsFactory.Create` / `CreateContext` and the resource types uploaded through it) — the backend and the host stay replaceable.

## 3. Install

```bash
# Engine kernel (scene / geometry / point-cloud / utils)
dotnet add package RobotSimulation.Core

# Robot domain model
dotnet add package RobotSimulation.Robot

# OpenGL render backend (Silk.NET + StbImageSharp)
dotnet add package RobotSimulation.OpenGL
```

> Version numbers and `PackageId` are set at first release; the repo currently targets `net8.0`.

## 4. Quick Start

### 4.1 Headless: parse URDF + forward kinematics (no rendering)

```csharp
using RobotSimulation.Robot;

// URDF text → pure-data description → headless joint state & FK
var state = RobotModel.Parse(File.ReadAllText("robot.urdf")).CreateState();

state.SetJointValue("shoulder", 1.2f);   // revolute: radians
state.SetJointValue("slider", 0.35f);     // prismatic: meters

Matrix4x4 eePose = state.GetLinkGlobalPose("tool0");
```

### 4.2 Visualization: build a scene + add a robot (rendering handled by the backend)

```csharp
using System.Numerics;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Robot;

var scene = new SceneGraph();                       // default camera / lights / grid (world axes are opt-in)

// A simple box primitive
scene.Add(new Box(1f, 0.5f, 0.2f, name: "base"));

// The robot: a GameObject tree, added as an ordinary drawable node
var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);
robot.SetJointValue("shoulder", 0.8f);

// Per-frame logic is injected, not subclassed (optional): any node can carry behaviors
var marker = new Box(0.2f, 0.2f, 0.2f, name: "marker");
scene.Add(marker);
float spin = 0;
marker.AddUpdate((go, dt) =>
    go.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin += (float)dt));

// Per frame: drive joints, run behaviors / update the scene, then render — update and render share one
// frame-loop thread, and SceneGraph.Add/Remove may also be called from any other thread (queued until then)
scene.Update(deltaTime);
```

### 4.3 Hook up a graphics backend (OpenGL example)

```csharp
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Device;

// Once you have a GL instance in your window / render context:
(IRenderContext context, IRenderer renderer) = GraphicsFactory.Create(gl);

context.Resize(viewportWidth, viewportHeight);      // viewport = pixel width / height
renderer.Render(scene);                             // per frame
```

> The composition root (window, input, creation of the GL instance) belongs to the host; the library only assembles a `GL` into an `IRenderContext` / `IRenderer`.

### 4.4 Embedding in a UI framework (Avalonia example)

The library never creates a window or a GL context, so a UI host only answers two questions: *where does `GL` come from* and *which framebuffer do I draw into*. An Avalonia embed looks like this:

```csharp
public class RobotViewportControl : OpenGlControlBase          // Avalonia hands out a context per control
{
    protected override void OnOpenGlInit(GlInterface gl)
    {
        GL silkGl = GL.GetApi(gl.GetProcAddress);               // wrap the host's proc addresses
        (_context, _renderer) = GraphicsFactory.Create(silkGl);  // the whole composition root
        _scene = new SceneGraph();
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer); // draw into the control
        _context.Resize(pixelWidth, pixelHeight);   // physical pixels = control size × RenderScaling
        _context.Clear(_scene.BackgroundColor);
        _renderer.Render(_scene);
        RequestNextFrameRendering();                // keep the frame loop running
    }
}
```

The library ships no runnable host application. The visual check lives in the test project: `Tests/BareWindowTests.cs` opens a Silk.NET window, renders a default `SceneGraph` (its sample scene includes curves with different line widths), and returns when the window is closed. It needs a display, so the CI test step filters it out.

## 5. Documentation

Fully split into Chinese / English, per module **and** for this index itself — [`docs/README.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/README.md) (English) / [`docs/README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/README.zh-CN.md) (中文).

| Doc | Contents |
|---|---|
| [`docs/architecture/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/architecture/en.md) | Packaging architecture: boundaries, dependency direction, runtime/render threading, ADRs, extension points, pack roadmap |
| [`docs/core/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/core/en.md) | `RobotSimulation.Core` public API reference (Scene / Geometry / Rendering / Utils) |
| [`docs/robot/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/robot/en.md) | `RobotSimulation.Robot` public API reference (RobotModel / Description / Urdf / State) |
| [`docs/opengl/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/opengl/en.md) | `RobotSimulation.OpenGL` public API + host composition + custom shader pipeline |

## 6. Conventions

- **Coordinates**: right-handed world, **Z-up**; camera up = +Z; primitive rotation axes along +Z (URDF/ROS semantics).
- **Units**: lengths in meters; angles / joint values in radians; colors as `Vector4` RGBA with components in `[0,1]`.
- **Render data**: `MeshData` / `MaterialData` are pure-CPU data, buildable on any thread; GPU resources are instantiated & cached by the backend on the render thread.
- **Threading**: `Update` and `Render` share one frame-loop thread (they are the scene's frame boundaries); `SceneGraph.Add` / `Remove` may be called from **any** thread — off the owner thread they are queued and commit at the next frame boundary, so a running scene can keep growing; `Logger` is available on any thread.
- **Dependency direction**: host → `OpenGL`/`Robot` → `Core`; `Core` never references `Robot` / `OpenGL` / any UI framework.

## 7. Roadmap

- [x] Packaging metadata (`PackageId` / version / NuGet metadata / README / license): `Directory.Build.props` plus each `.csproj`, verified with `dotnet pack` — 3 `.nupkg` + 3 `.snupkg`, each carrying its XML docs, the README and a `LICENSE` copy. Pushing to a feed stays a separate, manual step.
- [ ] `Visualization` display layer (rviz-like Display) and external write protocol (Sink).
- [ ] Three host samples / test projects: bare window, WPF, Avalonia — one `RobotViewport`-style control each, proving the library *can render standalone* and providing a place to host tests. **Descoped**: the standalone host apps were removed; the repo keeps only the `RobotSimulation.Tests` xunit project (with the windowed visual check `BareWindowTests`), so embedding a host is left to consumers.
- [ ] Unit tests: geometry primitives, ray picking, URDF parsing, `RobotState` FK. **In progress**: `Tests/` (xunit) holds `SceneGraphThreadingTests` — **10 headless checks** over the scene's threading contract (the owner is claimed once, cross-thread `Add` / `Remove` and `Transform.Parent` queue up and commit at a frame boundary, the queue cap and its drop counter, a root leaving `Roots` when it gains a parent) ✔ — plus `BareWindowTests`, a visual / manual check that really opens a window ✔. The earlier 37-check suite (URDF parsing and `IAssetResolver` path resolution, geometry primitives, ray picking, the default `SceneGraph` and its selection contract) went away with the old `src/RobotSimulation.Tests/` directory. Shapes / picking / path resolution and `RobotState` FK are still to come.
- [ ] (optional) An additional non-Silk render backend.

---

## 8. License

MIT — see [`LICENSE`](https://github.com/YH563/RobotSimulation/blob/master/LICENSE). The three library packages declare it with the SPDX expression `MIT` in
`Directory.Build.props`, so `dotnet pack` output states its terms without needing a license file inside the
package; a copy of `LICENSE` is packed alongside the README anyway.
