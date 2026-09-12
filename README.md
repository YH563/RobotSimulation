# RobotSimulation

> 简体中文版本见 [`README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/README.zh-CN.md) / For the Chinese version, see [`README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/README.zh-CN.md).

A self-contained, ROS-free, embeddable **robot 3D-visualization and simulation library for desktop UIs**. It cleanly decouples the *scene object model*, *pure-CPU render data*, and *robot domain model* from any *concrete graphics backend* and *UI framework*, ultimately targeting rviz-like capabilities. The same scene embeds in Avalonia / WPF / a custom-drawn window and runs headless (URDF parsing, forward kinematics, pose & point-cloud processing).

## 1. Packages

The repo is organized by the boundaries of *future publishable NuGet packages* — a directory is an assembly boundary. Three library assemblies plus the (unpublished) host projects:

| Assembly | Responsibility | Depends on |
|---|---|---|
| **`RobotSimulation.Core`** | Engine kernel: scene object model (`Scene`), pure-CPU geometry & model import (`Geometry`), render abstraction + material/texture description (`Rendering`), point cloud (`PointCloud2Data`), utils (`Utils`). Zero graphics, zero UI. | `Silk.NET.Assimp`, `Microsoft.Extensions.Logging` |
| **`RobotSimulation.Robot`** | Robot domain model: URDF / description (`Description`, `Urdf`), headless forward kinematics (`State`), robot `GameObject` tree (`RobotModel`). | `Core` |
| **`RobotSimulation.OpenGL`** | The only render backend: Silk.NET OpenGL + StbImageSharp meshes / materials / textures / shaders & renderer (`Device`, `Rendering`, `Resources`). | `Core` |
| *(test) `BareWindowTest`* | Bare-window host test: a minimal Silk.NET window with **no UI framework at all**. Doubles as the automated smoke test (`--smoke [frames]` → exit code 0/1). **Not published**. | `Core`, `Robot`, `OpenGL` |
| *(test) `AvaloniaTest`* | Avalonia host test: embeds the library in a `RobotViewportControl` (derived from `Avalonia.OpenGL.Controls.OpenGlControlBase`) and prints the GPU / frame-rate / smoke lines to the console with exactly the wording the bare-window host uses. **Not published**. | `Core`, `Robot`, `OpenGL`, `Avalonia` |
| *(test) `RobotSimulation.Tests`* | xunit checks over the published surface: `FileSystemAssetResolver` path resolution (root order, `package://` handling, hard failure on a miss) and end-to-end URDF → `RobotModel` loading, including the standard ROS layout that only loads with `assetDirectory`. **Not published**. | `Core`, `Robot` |
| *(data) `BareWindowTest/Assets`, `AvaloniaTest/Assets`* | Test data of each host test: one `Assets/` tree per project, copied next to that project's executable by its `.csproj`. Each host loads the **single URDF** named in its own source (`Models/fairino3_v6/`); the trees also carry ready-to-use samples (`Models/primitives.urdf`, `Models/urdf_tutorial/`, `Models/formats/`, `PointClouds/`) to switch to. No path arguments, no shared folder, no path table. `RobotSimulation.Tests` reads these trees in place and adds its own ROS-workspace fixture. | — |

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
- **Picking**: `Camera.ScreenToWorldRay` → `SceneGraph.Pick` / `PickAndHighlight` (with highlight feedback).
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
using System.Numerics;
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

// Per-frame logic is injected, not subclassed (optional): any node can carry behaviors
var marker = new Box(0.2f, 0.2f, 0.2f, name: "marker");
scene.Add(marker);
float spin = 0;
marker.AddUpdate((go, dt) =>
    go.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin += (float)dt));

// Each update: drive joints, run behaviors / update the scene (background thread), then Rendering on the render thread
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

The library never creates a window or a GL context, so a UI host only answers two questions: *where does `GL` come from* and *which framebuffer do I draw into*. `src/AvaloniaTest/Controls/RobotViewportControl.cs` is the reference implementation:

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

Both runnable host tests ship in this repo. They take **no data arguments at all**: each loads the **single URDF model** named in its own source — `Assets/Models/fairino3_v6/fairino3_v6.urdf`, copied next to the executable by the project file — and writes the same GPU / FPS / smoke lines to the console, so the two runs can be compared line by line:

```bash
# Either host: render 120 frames, print GPU + FPS, exit 0 — usable as a CI smoke test
dotnet run --project src/BareWindowTest -- --smoke 120
dotnet run --project src/AvaloniaTest  -- --smoke 120

# Without --smoke the same command opens the window and runs until it is closed
dotnet run --project src/AvaloniaTest
```

One run loads exactly one URDF (`fairino3_v6`); the grid floor, lights, world axes and camera pose all come from the library's default `SceneGraph`, and the window then offers orbit (drag), zoom (wheel / right-drag) and pick-to-highlight (click). The repository also ships further ready-to-use samples in `Assets/` — URDF built-in geometry, the community `urdf_tutorial` package, all five mesh formats, and two RGB point clouds — plus the scripts that regenerate them; swap one in by changing a single constant. See [`docs/testing/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/testing/en.md) for where the data comes from and how to add or download more.

## 5. Documentation

Fully split into Chinese / English, per module **and** for this index itself — [`docs/README.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/README.md) (English) / [`docs/README.zh-CN.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/README.zh-CN.md) (中文).

| Doc | Contents |
|---|---|
| [`docs/architecture/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/architecture/en.md) | Packaging architecture: boundaries, dependency direction, runtime/render threading, ADRs, extension points, pack roadmap |
| [`docs/core/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/core/en.md) | `RobotSimulation.Core` public API reference (Scene / Geometry / Rendering / Utils) |
| [`docs/robot/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/robot/en.md) | `RobotSimulation.Robot` public API reference (RobotModel / Description / Urdf / State) |
| [`docs/opengl/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/opengl/en.md) | `RobotSimulation.OpenGL` public API + host composition + custom shader pipeline |
| [`docs/testing/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/testing/en.md) | Test data guide: what the host tests load, where the files come from (incl. regenerating the samples and optional downloads) |

## 6. Conventions

- **Coordinates**: right-handed world, **Z-up**; camera up = +Z; primitive rotation axes along +Z (URDF/ROS semantics).
- **Units**: lengths in meters; angles / joint values in radians; colors as `Vector4` RGBA with components in `[0,1]`.
- **Render data**: `MeshData` / `MaterialData` are pure-CPU data, buildable on any thread; GPU resources are instantiated & cached by the backend on the render thread.
- **Threading**: scene mutation and `SceneGraph.Update` run on the update thread; `Render` runs only on the render thread; `Logger` is available on any thread.
- **Dependency direction**: host → `OpenGL`/`Robot` → `Core`; `Core` never references `Robot` / `OpenGL` / any UI framework.

## 7. Roadmap

- [x] Packaging metadata (`PackageId` / version / NuGet metadata / README / license): `Directory.Build.props` plus each `.csproj`, verified with `dotnet pack` — 3 `.nupkg` + 3 `.snupkg`, each carrying its XML docs, the README and a `LICENSE` copy. Pushing to a feed stays a separate, manual step.
- [ ] `Visualization` display layer (rviz-like Display) and external write protocol (Sink).
- [ ] Three host samples / test projects: bare window, WPF, Avalonia — one `RobotViewport`-style control each, proving the library *can render standalone* and providing a place to host tests. **In progress**: bare window (`src/BareWindowTest`) ✔ and Avalonia (`src/AvaloniaTest`) ✔; WPF pending.
- [ ] Unit tests: geometry primitives, ray picking, URDF parsing, `RobotState` FK. **In progress**: `src/RobotSimulation.Tests` (xunit, 14 checks) covers URDF parsing and `IAssetResolver` path resolution end to end — including the standard ROS layout that only loads with `assetDirectory` ✔; geometry primitives, ray picking and `RobotState` FK pending.
- [ ] (optional) An additional non-Silk render backend.

---

## 8. License

MIT — see [`LICENSE`](https://github.com/YH563/RobotSimulation/blob/master/LICENSE). The three library packages declare it with the SPDX expression `MIT` in
`Directory.Build.props`, so `dotnet pack` output states its terms without needing a license file inside the
package; a copy of `LICENSE` is packed alongside the README anyway.

This covers this repository's own source only. Bundled test data keeps its own terms — the `urdf_tutorial`
sample under `Assets/Models/` is BSD-3-Clause, as recorded in the per-folder READMEs and
[`docs/testing/en.md`](https://github.com/YH563/RobotSimulation/blob/master/docs/testing/en.md).
