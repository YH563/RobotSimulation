# RobotSimulation.Core Public API (English)

> Status: reflects the current code. Namespace: `RobotSimulation.Core.*` · Depends on: `Silk.NET.Assimp`, `Microsoft.Extensions.Logging`.
> Companion: `../architecture/en.md`, `../robot/en.md`, `../opengl/en.md`.

`Core` is the engine kernel: no graphics, no UI, no robot-domain semantics. It provides the scene object model, pure-CPU render data, render abstraction interfaces, point-cloud & model import, and general utilities. Colors are `Vector4` (RGBA, components `[0,1]`); the world is right-handed **Z-up**, lengths in meters, angles in radians.

---

## 1. Scene / Scene object model

### `GameObject`
A scene node. It holds only scene data (Transform, CPU model data, CPU material) and manages no GPU resources — GPU instantiation is done by the rendering backend at render time. It can thus be created on any thread and reused across scenes.

| Member | Type | Notes |
|---|---|---|
| `Name` | `string` | Name, for debugging and name lookup (e.g. URDF link/joint names) |
| `Transform` | `Transform` | This node's transform (read-only ref) |
| `MeshData` | `MeshData?` | Triangle mesh (CPU); `null` = skeleton/pure hierarchy node |
| `LineData` | `LineData?` | Line segment set (with `RenderPassKind.Line`) |
| `PointData` | `PointCloud2Data?` | Point cloud (with `RenderPassKind.Point`) |
| `PointSize` | `float` | Pixel size of the point pass (`GL_POINTS`), default 3 |
| `ShowLocalAxes` | `bool` | Whether to attach local axes (auto sub-object) |
| `LocalAxesLength` | `float` | Default local axes length, default 0.3 |
| `LocalAxes` | `Axes?` | Attached local axes (non-null when enabled) |
| `MaterialData` | `MaterialData?` | Material description (CPU); `null` = no draw/default |
| `Visible` | `bool` | Whether visible, default true |
| `Highlighted` | `bool` | Whether highlighted (default false), selection feedback |
| `HighlightColor` | `Vector4` | Highlight blend color, default orange |
| `Pickable` | `bool` | Whether it participates in ray picking, default true |
| `UpdateBehaviors` | `IReadOnlyList<IUpdateBehavior>` | Attached update behaviors (execution order; empty when none) |
| `UpdateBehaviorCount` | `int` | Number of attached update behaviors (0 = no per-frame work) |

Methods:
- ctor `GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")`
- `void SetSubtreePickable(bool value)` — recursively set `Pickable` on this node and descendants
- `void LoadModel(string filePath, LoadOptions? options = null)` — load geometry+material from a model file (STL/OBJ/DAE/glTF) into this node (single submesh; for multiple use `AssimpModelLoader.Load`)
- `void Update(double deltaTime)` — per-frame dispatch: runs the attached behaviors in attach order, skipping disabled ones (called by `SceneGraph.Update`, parent before child)
- `T AddBehavior<T>(T behavior) where T : IUpdateBehavior` — attach a behavior and return it unchanged (for later removal/pausing)
- `DelegateUpdateBehavior AddUpdate(Action<GameObject, double> update)` — inject per-frame logic as a lambda (receives `owner, dt`)
- `DelegateUpdateBehavior AddUpdate(Action<double> update)` — same, for updates that do not need the node
- `bool RemoveBehavior(IUpdateBehavior behavior)` — detach a behavior; returns whether it was attached
- `void ClearBehaviors()` — remove every attached update behavior

#### Update behaviors (composition over inheritance)

A node no longer grows by subclassing and overriding `Update`; it **carries** its logic: attach any number of `IUpdateBehavior` instances and `SceneGraph.Update` dispatches them once per frame, in attach order and parent before child. Logic is assembled at runtime, so it can be added/removed, paused, and unit-tested — and it works on `sealed` nodes or nodes produced by file parsing, with no subclass needed. Behaviors run on the update thread and may only mutate CPU data, never GL.

| Type | Location | Notes |
|---|---|---|
| `IUpdateBehavior` | `Core.Scene` | The contract: `bool Enabled { get; set; }` + `void Update(GameObject owner, double deltaTime)`; this is what the list holds |
| `DelegateUpdateBehavior` | `Core.Scene` | Sugar adapting a lambda to `IUpdateBehavior`; created and returned by `AddUpdate` |

```csharp
var box = new Box(new MeshData(), new MaterialData());
scene.Add(box);

float spin = 0;
box.AddUpdate((go, dt) =>                       // logic = one lambda, no subclass
{
    spin += (float)(dt * Math.PI);
    go.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin);
});
```

- One node can carry several unrelated behaviors (one lambda each); `RemoveBehavior` / `ClearBehaviors` detach them and the same instance can be re-attached later.
- Pause without detaching: set the `DelegateUpdateBehavior.Enabled` returned by `AddUpdate` to `false`; set it back to `true` to resume.
- Complex/reusable logic can live in a class implementing `IUpdateBehavior` and be attached with `AddBehavior<T>`; no behavior base class is provided today — add one only when needed.
- A node with no behaviors costs nothing extra: the list is created lazily and `Update` is a single null check.

### `Transform`
Parent/child hierarchy transform, row-major/row-vector. Supports read-only locking: after locking, the `Position` / `Rotation` / `Scale` setters throw `InvalidOperationException`; framework internals use `SetLocalPose` to bypass the guard.

| Member | Type | Notes |
|---|---|---|
| `Owner` | `GameObject` | Owner node |
| `Children` | `List<Transform>` | Child transforms |
| `Position` | `Vector3` | Local position (setter read-only protected) |
| `Rotation` | `Quaternion` | Local rotation (setter read-only protected) |
| `Scale` | `Vector3` | Local scale (setter read-only protected) |
| `IsReadOnly` | `bool` | Whether read-only |
| `Parent` | `Transform?` | Parent transform |

Methods:
- `Matrix4x4 GetLocalMatrix()` — local matrix = S·R·T
- `Matrix4x4 GetModelMatrix()` — world/model matrix (recursive through parents)
- `Matrix4x4 GetWorldInverseMatrix()` — world matrix inverse (returns `Identity` if not invertible)
- `Vector3 WorldToLocal(Vector3 worldPoint)` / `Vector3 LocalToWorld(Vector3 localPoint)`

### `SceneGraph`
Scene root container: holds the object tree, the active camera, lights, and scene display defaults (background/ambient/grid floor). The constructor auto-assembles the default camera pose, lights and grid floor; the world-origin axes are created only when `ShowWorldAxes` is on (off by default — the renderer's screen-space orientation gizmo answers "which way is X/Y/Z" without putting an occludable axes set in the scene). Selection is the same question asked about one node instead of the world: `Select` highlights what was picked and mounts that node's own local axes on it, so "what did I pick" and "which way does it point" arrive together. Those axes are **display only** — a plain child node with no drag affordance, and the scene never writes back to a node's transform, so reading a frame can never disturb a model whose poses belong to its joint chain.

| Member | Type | Notes |
|---|---|---|
| `Roots` | `IReadOnlyList<GameObject>` | Root nodes (render traversal & external read-only access) |
| `Camera` | `Camera` | Active camera (replaceable as a whole) |
| `BackgroundColor` | `Vector4` | Clear/background color, default mid-grey |
| `AmbientColor` | `Vector3` | Ambient light (RGB intensity 0-1), default dark |
| `ShowGrid` | `bool` | Show the default grid floor (default true) |
| `ShowWorldAxes` | `bool` | Show the default world-origin axes (default **false**: opt in when a world-scale ruler is wanted) |
| `WorldAxesLength` | `float` | World axes length in meters (default 0.5). A real length, because the default set is `AxesSizing.FixedWorldLength` |
| `ShowOrientationGizmo` | `bool` | Draw the screen-space orientation gizmo (default true) |
| `OrientationGizmoSize` / `OrientationGizmoMargin` | `float` | Gizmo square size / gap to the bottom-right edges, in pixels (defaults 160 / 16). The widget is just three arrows and a hub ball — it carries no backdrop |
| `ShowSelectionAxes` | `bool` | Show the selected object's own local axes (default true; display only — a marker, never a drag handle) |
| `SelectionAxesLength` | `float` | Reference length (m) of the axes mounted on the selected object, default 0.3 |
| `Selected` | `GameObject?` | The currently selected object (read-only; change it through `Select` / `PickAndSelect`) |
| `GridCellSize` / `GridCellCount` / `GridColor` | `float` / `int` / `Vector4` | Grid floor: cell edge (m) / cells per side / line color |
| `Lights` | `IReadOnlyList<Light>` | Scene lights (collected by `Add`) |

Methods:
- `void Add(GameObject? obj)` — add to the scene (root nodes are registered automatically; a `Light` also joins `Lights`)
- `void Remove(GameObject? obj)` — remove from the scene (a `Light` also leaves `Lights`)
- `Update(double deltaTime)` — recurse into each node `Update` (update thread)
- Default assembly: `Grid? AddDefaultGrid()`, `void AddDefaultLights()`, `Axes? AddDefaultWorldAxes()`, `void ApplyDefaultCamera()` (the constructor already calls the first three, honouring `ShowGrid` / `ShowWorldAxes`)
- `float FitWorldAxesToContent(float factor = 1.15f)` — size the axes to the content's largest world extent × `factor` (the axes, grid and lights are ignored while measuring), so they reach past the model instead of hiding inside it; applies to the set already in the scene and returns the applied length. `WorldAxesName` is the name it looks for
- Picking: `RaycastHit? Pick(Ray ray, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)`, `GameObject? PickAndHighlight(Ray ray, bool enable = true, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)`
- Selection: `GameObject? Select(GameObject? obj, bool highlight = true)` — single selection with feedback: the new object takes the highlight and (with `ShowSelectionAxes`) its own local axes, mounted non-pickable so they never swallow a later click; the previous object loses both, and null clears. Only the axes `Select` mounted itself are unmounted. The marker displays a frame and never edits one — there are no drag handles anywhere in the library, so a pick can't touch a robot's joint-driven poses
- `GameObject? PickAndSelect(Ray ray, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)` — `Pick` + `Select` in one call: a host's whole click path
- `void Dispose()` — drops the node and light lists (GPU resources are released by the renderer)

### `Camera`
A "display-state object" holding only math state; not a scene. Replaceable as a whole via `SceneGraph.Camera`.

Properties: `Yaw`, `Pitch` (degrees, `Pitch` clamped to [-89, 89] so the view cannot flip), `Distance` (clamped to [0.5, 200]), `Target` (orbit centre), `Position` (read-only, derived from the orbit state and used as the ray origin of `ScreenToWorldRay`), `Fov`, `AspectRatio` (written by the host per frame / on resize), `NearPlane`, `FarPlane`.
Methods:
- `Matrix4x4 GetViewMatrix()` / `Matrix4x4 GetProjectionMatrix()` — the projection uses the `AspectRatio` property (there is no aspect-taking overload)
- Gestures: `Rotate(float deltaYawDeg, float deltaPitchDeg)` (**yaw first, then pitch**), `Pan(Vector2 screenDelta)` (screen pixels: +X right, +Y up), `Zoom(float delta)` (positive moves closer), `Reset()`
- `Ray ScreenToWorldRay(Vector2 screenPositionPixels, Vector2 viewportSizePixels)` — the picking entry point: screen pixels (top-left origin, +X right, +Y down) → world ray. **Both arguments are pixels**, and the viewport must be the one the renderer drew into (pixels → NDC is the ratio of the two), or the ray is tilted off the cursor

### `Light`
`enum LightType { Directional, Point }`. Members: `Type`, `Color` (`Vector3` intensity), `Direction` / `Position`, `Intensity`, `Range`. Exposes `WorldPosition` / `WorldDirection` (for rendering).

### `GameObject` primitives (`Scene/Primitives/`)
All `GameObject` subclasses; the ctor generates CPU mesh + material, ready for `scene.Add`:
- `Box(width, height, depth, name?)` / `Sphere(radius, name?)` / `Cylinder(radius, height, name?)` (axis +Z) / `Capsule(radius, height, name?)` (axis +Z; total height = height + 2×radius)
- `GroundPlane(size, name?)` / `Arrow(...)` / `Axes(length, name?)` / `Grid(size, spacing, name?)` / `Curve(...)` / `PointCloud(...)`
  - `Axes` has two sizing policies (`AxesSizing`): `ConstantScreenSize` (default, a marker whose screen size is fixed by `ScreenScale`/`MinWorldLength`/`MaxWorldLength`) and `FixedWorldLength` (the arrows measure exactly `Length` world units). `Length` stays writable in both, because the axes shader normalises by the arrow's own length.

---

## 2. Geometry & Data (`Geometry/`)

### Ray & bounds
- `Ray`: `Origin` plus a unit `Direction` (normalized by the constructor; a zero vector throws `ArgumentException`), so `t` is a world-unit distance and comparable across objects.
- `RaycastHit`: `Object` (`GameObject`), `Point` (world hit point), `Distance` (along the ray, world units), `Normal` (world normal, facing the ray), `U` / `V` (barycentric coordinates of the hit triangle).
- `Camera.ScreenToWorldRay` produces a `Ray`; `SceneGraph.Pick*` consumes one.

### `Raycast` (static; a nullable return means a miss)
- `float? HitSphere(in Ray ray, Vector3 center, float radius)`
- `float? HitPlane(in Ray ray, Vector3 point, Vector3 normal)` — null when parallel or behind the ray
- `float? HitAABB(in Ray ray, in Bounds bounds)` — slab broad phase; a negative distance means the origin is inside the box (still a hit)
- `bool HitTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 normal, out float u, out float v)` — Möller–Trumbore, double-sided (the normal flips toward the ray on a back-face hit)

### `Bounds`
- Ctor `Bounds(Vector3 min, Vector3 max)` (no validation; the caller guarantees `min <= max`), `static Bounds FromPoints(IEnumerable<Vector3>)` (an empty set yields the empty box `Min = Max = 0`).
- Properties `Min` / `Max` / `Center` / `Size`.

### Point cloud `PointCloud2Data` / `PointField` / `PointFieldDataType`
ROS `sensor_msgs/PointCloud2` style: a flat byte buffer + field descriptions. Each point occupies `PointStep` bytes; channels (x/y/z/rgb/intensity) are located by `PointField` (offset + datatype + count). Position channels (x/y/z) are required; color is optional, resolved by rgb/rgba → per-channel r/g/b → intensity (grey) priority.

| Member | Notes |
|---|---|
| `FrameId` | Optional origin/frame id |
| `Fields` / `PointStep` | Field descriptions / bytes per point |
| `Width` / `Height` / `Count` / `IsOrganized` | Organization / point count / organized (Height>1) |
| `Capacity` / `FirstSlot` | Slot count / physical slot of logical point 0 (the oldest) |
| `Revision` / `Dirty` | Content revision / change window accumulated since the last `ResetDirty` (`DirtyWindow`) |
| `HasColor` | Whether a parseable color channel exists |

Methods: ctor `(PointField[] fields, byte[] data, int pointStep, string? frameId = null, int width = 0, int height = 1)`, `float[] ToPositionArray()`, `float[]? ToColorArray()` (null if no color channel; renderer uses the material's uniform color), static `FromPositions(IEnumerable<Vector3>, frameId?)` (wraps an existing buffer, every slot is a point), static `CreateMutable(int initialCapacity = 0, bool withColor = false, string? frameId = null)` (a growable cloud whose store is reserved but still empty; `withColor` gives the layout an r/g/b float channel).

**Incremental updates** (for a live sensor stream / incremental mapping). Writes: `AddPoint(p)`, `AddPoint(p, color)`, `AddPoints(ReadOnlySpan<Vector3>)`, `AddPoints(positions, colors)`, `AppendRawPoint(ReadOnlySpan<byte>)` (one pre-encoded point, copied verbatim), `SetPoint(i, p)`, `SetColor(i, c)`; removals: `RemoveOldest(n)`, `TrimTo(max)`, `Clear()`; capacity: `EnsureCapacity(n)`. Appending writes at the write cursor and eviction only advances `FirstSlot`, so the data of existing points is neither moved nor re-uploaded.

- **Logical index** 0 is the oldest point and `Count-1` the newest; the public API is logical throughout. **Physical slots** follow from `FirstSlot` / `Capacity` and exist for the backend: `GetSlotRuns(start, count, runs)` splits a logical range into at most two contiguous slot runs, `CopySlotPositions` / `CopySlotColors` export by slot (while `CopyPositionsTo` / `CopyColorsTo` / `ToPositionArray` export in logical order).
- **Change window**: `Revision` increments on every content change; `Dirty` reports the logical range `[Start, Start+Count)` plus the revision the window started at (`FromRevision`). A backend that has already uploaded up to `FromRevision` only has to upload that range, anything else uploads everything; after uploading it calls `ResetDirty()`. Eviction deliberately leaves the window start alone (otherwise the ordinary "evict then append" frame would degrade into a full re-upload) but shifts the window's indices with the points; growth unrolls the ring (`FirstSlot` back to 0) and marks every valid point dirty.
- **Constraints**: an organized cloud (height > 1) refuses appends (`InvalidOperationException`); an append writes only the position and zero-fills the other channels (`AddPoint(p, color)` / `AppendRawPoint` pass values explicitly); the cached min/max of the intensity greyscale mapping is invalidated by writes and evictions, so the next color read recomputes it.

- `PointField`: `Name` / `Offset` / `DataType` / `Count`; static `GetElementSize(type)`; `Size`.
- `PointFieldDataType`: `Int8/UInt8/Int16/UInt16/Int32/UInt32/Float32/Float64` (values match ROS datatypes).

### `PointCloudIo`
Load point clouds from file: static `Load(string path, string? frameId = null)` picks a parser by extension (`.pcd` / `.ply`); `ReadPcd` / `ParsePcd` / `ReadPly` / `ParsePly`. `binary_compressed` is not supported and throws `NotSupportedException`.

### Model import `Import/`
- `AssimpModelLoader` (static): `LoadedModel Load(string filePath, LoadOptions? options = null)`. Uses Assimp through the `Silk.NET.Assimp` bindings; pipeline includes triangulation, auto UV/normal/tangent, vertex merge, validation, and cache-friendly ordering.
- `LoadOptions`: `Default`, `FlipUvV`, `FlipWinding`, `GlobalScale` (unit correction; URDF mesh scale belongs to Transform, not baked here).
- `LoadedModel`: `FilePath`, `Meshes` (`IReadOnlyList<LoadedMesh>`). `LoadedMesh`: `Name`, `MeshData`, `MaterialData`.
- **Native library**: the Assimp binaries ship per-RID inside the `Silk.NET.Assimp` package, so a host has nothing to install (and, unlike the earlier `AssimpNet`-based implementation, no `libdl.so` compatibility link is needed). The binding itself only looks the library up by bare name on the OS search path, which misses the `runtimes/<rid>/native` copy sitting next to the application, so `AssimpModelLoader` maps that copy by absolute path before asking for its function table — a host still has nothing to prepare.

---

## 3. Rendering / Abstraction & data

### Interfaces
- `IRenderContext : IDisposable`: `event Action<int,int>? Resized`, `void Resize(int width, int height)`, `void Clear(Vector4 clearColor, bool clearDepth = true)`, `GraphicsDeviceInfo DeviceInfo`.
- `IRenderer : IDisposable`: `void Render(SceneGraph scene)` (must only be called from the render thread), `FrameStats Stats`.

### `FrameStats` (record struct)
Frame timing reported by a renderer. Pure data (no graphics-API types), so any backend can produce it and any host (Avalonia / WPF / bare window) can draw its own FPS overlay.

| Member | Notes |
|---|---|
| `Fps` | Smoothed frames per second (from the interval between render calls) |
| `LastFrameMilliseconds` | Wall-clock time of the most recent frame |
| `AverageFrameMilliseconds` | Smoothed average frame time |
| `FrameCount` | Total frames rendered since the renderer was created |
| `Empty` | `static` zeroed value (before the first frame) |

The backend updates it on each render call (render thread); the host only reads it.

### `GraphicsDeviceInfo` (record struct)
Static device/driver information for diagnostics and support ("which GPU/driver is this running on?"). Pure data (all strings).

| Member | Notes |
|---|---|
| `Vendor` | Device vendor string (GL_VENDOR equivalent) |
| `Renderer` | Renderer/GPU name string (GL_RENDERER equivalent) |
| `ApiVersion` | Graphics API version string (GL_VERSION equivalent) |
| `ShaderVersion` | Shading language version string (GL_SHADING_LANGUAGE_VERSION equivalent) |
| `Unknown` | `static` empty value (backend cannot report it) |

Filled by the backend from the current context; upper layers only display or log it.

### `MaterialData`
Material description (CPU data), no GPU/shader state — GPU material instantiation and texture upload are done by the render backend at render time.

| Member | Notes |
|---|---|
| `BaseColor` | RGBA base color, components [0,1] |
| `MetallicFactor` / `RoughnessFactor` | Metallic/roughness [0,1] |
| `AlbedoTexture` / `NormalTexture` / `MetallicTexture` / `RoughnessTexture` | `TextureReference?` texture slots |
| `DoubleSided` | Whether double-sided (back-face culling off) |
| `Wireframe` | Whether wireframe |
| `PassKind` | `RenderPassKind`, default `Model` |

### `TextureReference` (`record`) & `TextureColorSpace`
CPU-side texture reference describing only "which file, what semantics", holding no GPU state.

- `record TextureReference(string? FilePath = null, byte[]? ImageData = null, TextureColorSpace ColorSpace = TextureColorSpace.Srgb, bool GenerateMipmaps = true)`
- `bool IsValid` (`HasFileData || HasMemoryData`), `HasFileData`, `HasMemoryData`
- `static FromFile(string filePath, TextureColorSpace, bool)` , `static FromData(byte[] data, TextureColorSpace, bool)`
- `enum TextureColorSpace { Srgb, Linear }`

### `RenderPassKind`
`Model=0, Line=1, Point=2, Skybox=3, Axes=4`. `Core` only defines "which passes exist"; each pass's GLSL implementation is supplied by the backend `EmbeddedShaders`.

---

## 4. Utils

| Type | Notes |
|---|---|
| `GameTimer` | Background fixed-frame-rate ticker: `TargetFps` (default 60), `Start/Stop`, `Tick(Action<float>)`, `Dispose` |
| `Logger` | Process-level silent logging facade (thin wrapper over `Microsoft.Extensions.Logging`): `Initialize(Action<ILoggingBuilder>?)`, `Shutdown`, `OnLog`, `MinimumLevel`, `IsEnabled`, `UiContext`, `Trace/Debug/Info/Warning/Error/Critical`, `GetLogger` |
| `MathUtils` | Degree/radian conversion (`DegreesToRadians`, `RadiansToDegrees`), `Clamp`, `Lerp`, `NormalizeAngle`, constants `Pi`/`TwoPi`/`DegToRad`/`RadToDeg` |

---

## 5. Anti-patterns

| Anti-pattern | Reason |
|---|---|
| public returns/accepts `GL`/`Mesh`/`ShaderProgram`/`GraphicsContext` | breaks cross-backend / cross-UI portability |
| public field exposes internal `Transform` direct mutation (as a normal pattern) | bypasses read-only protection; unclear threading/ownership |
| putting GL/UI types in data models | models must be serializable and headless |
| implementing app interactions (pick menu/panel) inside the library | interaction belongs to the host UI framework |
