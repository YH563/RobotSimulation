# RobotSimulation

> 简体中文版本见 [`README.zh-CN.md`](README.zh-CN.md) / For the Chinese version, see [`README.zh-CN.md`](README.zh-CN.md).

A self-contained, ROS-free, embeddable **robot 3D-visualization and simulation library for desktop UIs**. It cleanly decouples the *scene object model*, *pure-CPU render data*, and *robot domain model* from any *concrete graphics backend* and *UI framework*, ultimately targeting rviz-like capabilities. The same scene embeds in Avalonia / WPF / a custom-drawn window and runs headless (URDF parsing, forward kinematics, pose & point-cloud processing).

## 1. Packages

The repo is organized by the boundaries of *future publishable NuGet packages* — a directory is an assembly boundary. Three library assemblies plus one sample host:

| Assembly | Responsibility | Depends on |
|---|---|---|
| **`RobotSimulation.Core`** | Engine kernel: scene object model (`Scene`), pure-CPU geometry & model import (`Geometry`), render abstraction + material/texture description (`Rendering`), point cloud (`PointCloud2Data`), utils (`Utils`). Zero graphics, zero UI. | `AssimpNet`, `Microsoft.Extensions.Logging` |
| **`RobotSimulation.Robot`** | Robot domain model: URDF / description (`Description`, `Urdf`), headless forward kinematics (`State`), robot `GameObject` tree (`RobotModel`). | `Core` |
| **`RobotSimulation.OpenGL`** | The only render backend: Silk.NET OpenGL + StbImageSharp meshes / materials / textures / shaders & renderer (`Device`, `Rendering`, `Resources`). | `Core` |
| *(sample) `RobotSimulation.Host`* | Sample host: window + input + composition root. Demo only; **not published**. | `Core`, `Robot`, `OpenGL` |

> Packaging metadata (`PackageId` / `Version` / `readme`) is to be completed before the first release. The `src/{Core,Robot,OpenGL}` split already equals the three future NuGet packages.

## 2. Feature Highlights

- **Scene object model**: `GameObject` + `Transform` hierarchy, `SceneGraph` root container, `Camera` (orbit), `Light` (point / directional).
- **Visualization primitives**: `Box` / `Sphere` / `Cylinder` / `Capsule` / `GroundPlane` / `Arrow` / `Axes` / `Grid` / `Curve` / `PointCloud`.
- **Pure-CPU render data**: `MeshData` / `LineData` / `PointCloud2Data` / `MaterialData` / `TextureReference`, thread-friendly and reusable.
- **Render abstraction**: `IRenderContext` / `IRenderer`, `GraphicsFactory` composition root; `OpenGL` is the only implementation.
- **Model import**: `AssimpModelLoader` (STL / OBJ / DAE / glTF), point clouds `PointCloudIo` (PCD / PLY).
- **Robotics**: `RobotModel` (URDF → robot `GameObject` tree, itself a `GameObject`), `RobotState` (headless FK), pure-data descriptions, extensible `IAssetResolver`.
- **Custom shaders**: the OpenGL backend ships a built-in GLSL pipeline (Model / Line / Point / Skybox / Axes) as embedded resources, and the architecture is deliberately shaped so shading can become a first-class, game-engine-like extension point (see `docs/opengl/en.md`; per-node custom-shader injection is a planned next step).
- **Picking**: `Camera.ScreenToWorldRay` → `SceneGraph.Pick` / `PickAndHighlight` (with highlight feedback).
- **Cross-UI embedding**: the public API never exposes `Silk.NET.*` / OpenGL / window types; the backend and host are fully replaceable.

## 3. Install

```bash
# Engine kernel (scene / geometry / point-cloud / utils)
dotnet add package RobotSimulation.Core

# Robot domain model
dotnet add package RobotSimulation.Robot

# OpenGL render backend (Silk.NET + StbImageSharp)
dotnet add package RobotSimulation.OpenGL
```

> Version numbers and `PackageId` are set at first release; the repo currently targets `net10.0`.

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
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Robot;

var scene = new SceneGraph();                       // default camera / lights / grid / axes

// A simple box primitive
scene.Add(new Box(1f, 0.5f, 0.2f, name: "base"));

// The robot: a GameObject tree, added as an ordinary drawable node
var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);
robot.SetJointValue("shoulder", 0.8f);

// Each update: drive joints, update the scene (background thread), then Rendering on the render thread
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

## 5. Documentation

Fully split into Chinese / English per module — see [`docs/README.md`](docs/README.md).

| Doc | Contents |
|---|---|
| [`docs/architecture/en.md`](docs/architecture/en.md) | Packaging architecture: boundaries, dependency direction, runtime/render threading, ADRs, extension points, pack roadmap |
| [`docs/core/en.md`](docs/core/en.md) | `RobotSimulation.Core` public API reference (Scene / Geometry / Rendering / Utils) |
| [`docs/robot/en.md`](docs/robot/en.md) | `RobotSimulation.Robot` public API reference (RobotModel / Description / Urdf / State) |
| [`docs/opengl/en.md`](docs/opengl/en.md) | `RobotSimulation.OpenGL` public API + host composition + custom shader pipeline |

## 6. Conventions

- **Coordinates**: right-handed world, **Z-up**; camera up = +Z; primitive rotation axes along +Z (URDF/ROS semantics).
- **Units**: lengths in meters; angles / joint values in radians; colors as `Vector4` RGBA with components in `[0,1]`.
- **Render data**: `MeshData` / `MaterialData` are pure-CPU data, buildable on any thread; GPU resources are instantiated & cached by the backend on the render thread.
- **Threading**: scene mutation and `SceneGraph.Update` run on the update thread; `Render` runs only on the render thread; `Logger` is available on any thread.
- **Dependency direction**: `Host → OpenGL/Robot → Core`; `Core` never references `Robot` / `OpenGL` / any UI framework.

## 7. Roadmap

- [ ] Publish packaging (`PackageId` / version / NuGet metadata).
- [ ] `Visualization` display layer (rviz-like Display) and external write protocol (Sink).
- [ ] Three host samples / test projects: bare window, WPF, Avalonia — one `RobotViewport`-style control each, proving the library *can render standalone* and providing a place to host tests.
- [ ] Unit tests: geometry primitives, ray picking, URDF parsing, `RobotState` FK.
- [ ] (optional) An additional non-Silk render backend.

---

## 8. License

(TODO) Add license before publishing; a permissive license (e.g. MIT) is intended.
