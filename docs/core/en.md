# RobotSimulation.Core Public API (English)

> Status: reflects the current code. Namespace: `RobotSimulation.Core.*` · Depends on: `AssimpNet`, `Microsoft.Extensions.Logging`.
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

Methods:
- ctor `GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")`
- `void SetSubtreePickable(bool value)` — recursively set `Pickable` on this node and descendants
- `void LoadModel(string filePath, LoadOptions? options = null)` — load geometry+material from a model file (STL/OBJ/DAE/glTF) into this node (single submesh; for multiple use `AssimpModelLoader.Load`)
- `virtual void Update(double deltaTime)` — per-frame update hook

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
Scene root container: holds the object tree, the active camera, lights, and scene display defaults (background/ambient/grid floor). The constructor auto-assembles the default camera pose, lights, grid floor, and world axes.

| Member | Type | Notes |
|---|---|---|
| `Roots` | `IReadOnlyList<GameObject>` | Root nodes (render traversal & external read-only access) |
| `Camera` | `Camera` | Active camera (replaceable as a whole) |
| `BackgroundColor` | `Vector4` | Clear/background color, default mid-grey |
| `AmbientColor` | `Vector3` | Ambient light (RGB intensity 0-1), default dark |
| `ShowGrid` / `ShowWorldAxes` | `bool` | Show default grid / world axes |
| `WorldAxesLength` | `float` | World axes length (default 1.0) |

Methods:
- `void Add(GameObject go)` / `bool Remove(GameObject go)` / `void Clear()`
- `GameObject Find(string name)` — find by name (depth-first)
- `IReadOnlyList<GameObject> FindAll(string name)` — find all by name
- `IEnumerable<GameObject> EnumerateAll()` — enumerate the whole tree
- `Update(double deltaTime)` — recurse into each node `Update` + camera/model update (update thread)
- Picking: `bool Pick(Ray ray, out RaycastHit hit, Func<GameObject,bool>? predicate = null)`, `bool PickAndHighlight(Ray ray, Color? color = null, ...)`, `ClearHighlight()`
- `SetWorldAxes(bool visible, float length)` / `ShowGrid(bool visible)`

### `Camera`
A "display-state object" holding only math state; not a scene. Replaceable as a whole via `SceneGraph.Camera`.

Properties: `Position`, `Target`, `Up`, `NearPlane`, `FarPlane`, `Fov`, `Orthographic`, `OrbitDistance`, `ClipFarAllowed`, etc.
Methods:
- `Vector3 Forward` / `Vector3 Right` / `Vector3 Up` / `Vector3 Eye` (unit vectors)
- `Matrix4x4 GetViewMatrix()` / `Matrix4x4 GetProjectionMatrix(float aspect)` / `GetViewProjection(aspect)`
- Gestures: `Rotate(float pitchDelta, float yawDelta, float distanceDelta = 0)`, `Pan(Vector2 delta)`, `Zoom(...)`, `Reset()`
- `Ray ScreenToWorldRay(Vector2 ndc)` — picking entry point; `Vector3 ScreenToWorld(Vector3 ndc)`

### `Light`
`enum LightType { Directional, Point }`. Members: `Type`, `Color` (`Vector3` intensity), `Direction` / `Position`, `Intensity`, `Range`. Exposes `WorldPosition` / `WorldDirection` (for rendering).

### `GameObject` primitives (`Geometry/`)
All `GameObject` subclasses; the ctor generates CPU mesh + material, ready for `scene.Add`:
- `Box(width, height, depth, name?)` / `Sphere(radius, name?)` / `Cylinder(radius, height, name?)` (axis +Z) / `Capsule(radius, height, name?)` (axis +Z; total height = height + 2×radius)
- `GroundPlane(size, name?)` / `Arrow(...)` / `Axes(length, name?)` / `Grid(size, spacing, name?)` / `Curve(...)` / `PointCloud(...)`

---

## 2. Geometry & Data (`Geometry/`)

### Ray & bounds
- `RaycastHit`: `Hit`(bool), `Distance`, `Point`, `Normal`, `GameObject`.
- `Ray` is the output of `Camera`/`ScreenToWorldRay`; `SceneGraph.Pick*` takes a `Ray`.

### `Raycast` (static)
- `bool HitBounds(Bounds bounds, Ray ray, out float distance)`
- `bool HitMesh(MeshData mesh, Ray ray, out float distance, out Vector3 normal, out float u, out float v)` — on a transformed mesh
- `bool HitSphere(Vector3 center, float radius, Ray ray, out float distance, out Vector3 normal)` — kept as a general utility
- `bool HitPlane(Vector3 point, Vector3 normal, Ray ray, out float distance)` — kept as a general utility
- `bool IntersectTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 normal, out float u, out float v)` — Möller-Trumbore, double-sided

### `Bounds`
- `Bounds(Vector3 min, Vector3 max)`, `CreateFromPoints(IEnumerable<Vector3>)`, `static Bounds FromPoints(...)`.
- `Min` / `Max` / `Center` / `Extents` / `Radius`, `Contains(Vector3)`, `Encapsulate(Vector3)` / `Encapsulate(Bounds)`, `IsEmpty`.

### Point cloud `PointCloud2Data` / `PointField` / `PointFieldDataType`
ROS `sensor_msgs/PointCloud2` style: a flat byte buffer + field descriptions. Each point occupies `PointStep` bytes; channels (x/y/z/rgb/intensity) are located by `PointField` (offset + datatype + count). Position channels (x/y/z) are required; color is optional, resolved by rgb/rgba → per-channel r/g/b → intensity (grey) priority.

| Member | Notes |
|---|---|
| `FrameId` | Optional origin/frame id |
| `Fields` / `PointStep` | Field descriptions / bytes per point |
| `Width` / `Height` / `Count` / `IsOrganized` | Organization / point count / organized (Height>1) |
| `HasColor` | Whether a parseable color channel exists |

Methods: ctor `(PointField[] fields, byte[] data, int pointStep, string? frameId = null, int width = 0, int height = 1)`, `float[] ToPositionArray()`, `float[]? ToColorArray()` (null if no color channel; renderer uses the material's uniform color), static `FromPositions(IEnumerable<Vector3>, frameId?)`.

- `PointField`: `Name` / `Offset` / `DataType` / `Count`; static `GetElementSize(type)`; `Size`.
- `PointFieldDataType`: `Int8/UInt8/Int16/UInt16/Int32/UInt32/Float32/Float64` (values match ROS datatypes).

### `PointCloudIo`
Load point clouds from file: static `Load(string path, string? frameId = null)` picks a parser by extension (`.pcd` / `.ply`); `ReadPcd` / `ParsePcd` / `ReadPly` / `ParsePly`. `binary_compressed` is not supported and throws `NotSupportedException`.

### Model import `Import/`
- `AssimpModelLoader` (static): `LoadedModel Load(string filePath, LoadOptions? options = null)`. Uses Assimp; pipeline includes triangulation, auto UV/normal/tangent, vertex merge, validation, and cache-friendly ordering.
- `LoadOptions`: `Default`, `FlipUvV`, `FlipWinding`, `GlobalScale` (unit correction; URDF mesh scale belongs to Transform, not baked here).
- `LoadedModel`: `FilePath`, `Meshes` (`IReadOnlyList<LoadedMesh>`). `LoadedMesh`: `Name`, `MeshData`, `MaterialData`.
- `AssimpNative` (static): `bool EnsureRuntime()` — establishes a libdl compatibility link for the Assimp native library on Linux (invoked by the host/tool composition root; the library does not auto-run it).

---

## 3. Rendering / Abstraction & data

### Interfaces
- `IRenderContext : IDisposable`: `event Action<int,int>? Resized`, `void Resize(int width, int height)`, `void Clear(Vector4 clearColor, bool clearDepth = true)`.
- `IRenderer : IDisposable`: `void Render(SceneGraph scene)` (must only be called from the render thread).

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
