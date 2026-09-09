# RobotSimulation.Core 公共 API（简体中文）

> 状态：反映当前实际代码。命名空间：`RobotSimulation.Core.*` · 依赖：`AssimpNet`、`Microsoft.Extensions.Logging`。
> 配套：`../architecture/zh-CN.md`、`../robot/zh-CN.md`、`../opengl/zh-CN.md`。

`Core` 是引擎内核，零图形、零 UI、零机器人领域语义。它提供：场景对象模型、纯 CPU 渲染数据、渲染抽象接口、点云与模型导入、以及通用工具。所有颜色统一用 `Vector4`（RGBA，分量 `[0,1]`），坐标系为右手系 **Z-up**，长度单位米，角度单位弧度。

---

## 1. Scene / 场景对象模型

### `GameObject`
场景节点。只持有场景数据：`Transform`、模型数据（CPU）、材质描述（CPU），不管理任何 GPU 资源——网格/材质的 GPU 实例化由渲染后端在渲染时完成。因而可在任意线程创建、跨场景复用。

| 成员 | 类型 | 说明 |
|---|---|---|
| `Name` | `string` | 名称，用于调试和按名查找（如 URDF link/joint 名） |
| `Transform` | `Transform` | 本节点变换（只读引用） |
| `MeshData` | `MeshData?` | 三角网格（CPU）；`null` 表示骨架/纯层级节点 |
| `LineData` | `LineData?` | 线段集（配合 `RenderPassKind.Line`） |
| `PointData` | `PointCloud2Data?` | 点云（配合 `RenderPassKind.Point`） |
| `PointSize` | `float` | 点通道的像素尺寸（`GL_POINTS`），默认 3 |
| `ShowLocalAxes` | `bool` | 是否挂载本节点局部坐标轴（自动子对象） |
| `LocalAxesLength` | `float` | 局部坐标轴默认长度，默认 0.3 |
| `LocalAxes` | `Axes?` | 已挂载的局部坐标轴（非 null 当启用后） |
| `MaterialData` | `MaterialData?` | 材质描述（CPU）；`null` 表示不绘制/默认外观 |
| `Visible` | `bool` | 是否可见，默认 true |
| `Highlighted` | `bool` | 是否高亮（默认 false），用于选择反馈 |
| `HighlightColor` | `Vector4` | 高亮混合色，默认橙黄 |
| `Pickable` | `bool` | 是否参与射线拾取，默认 true |

方法 / Methods:
- 构造 `GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")`
- `void SetSubtreePickable(bool value)` —— 递归设置本节点及后代的 `Pickable`
- `void LoadModel(string filePath, LoadOptions? options = null)` —— 从模型文件（STL/OBJ/DAE/glTF）加载几何+材质到本节点（仅支持单 submesh；多 submesh 用 `AssimpModelLoader.Load`）
- `virtual void Update(double deltaTime)` —— 每帧更新钩子

### `Transform`
父子层级变换，行主序/行向量。支持只读锁定：锁定后 `Position` / `Rotation` / `Scale` 的 setter 抛 `InvalidOperationException`；框架/系统内部经 `SetLocalPose` 绕过保护（内部使用）。

| 成员 | 类型 | 说明 |
|---|---|---|
| `Owner` | `GameObject` | 所有者节点 |
| `Children` | `List<Transform>` | 子变换列表 |
| `Position` | `Vector3` | 局部位置（setter 受只读保护） |
| `Rotation` | `Quaternion` | 局部旋转（setter 受只读保护） |
| `Scale` | `Vector3` | 局部缩放（setter 受只读保护） |
| `IsReadOnly` | `bool` | 是否只读 |
| `Parent` | `Transform?` | 父变换 |

方法 / Methods:
- `Matrix4x4 GetLocalMatrix()` —— 局部矩阵 = S·R·T
- `Matrix4x4 GetModelMatrix()` —— 世界/模型矩阵（含父级递归）
- `Matrix4x4 GetWorldInverseMatrix()` —— 世界矩阵逆（不可逆时返 `Identity`）
- `Vector3 WorldToLocal(Vector3 worldPoint)` / `Vector3 LocalToWorld(Vector3 localPoint)`

### `SceneGraph`
场景根容器：持有对象树、活动相机、灯光集合与场景显示默认值（背景/环境光/网格地面）。构造函数自动装配默认相机姿态、默认灯光、网格地面与世界坐标轴。

| 成员 | 类型 | 说明 |
|---|---|---|
| `Roots` | `IReadOnlyList<GameObject>` | 根节点（渲染遍历与外部只读访问） |
| `Camera` | `Camera` | 活动相机（可整体替换） |
| `BackgroundColor` | `Vector4` | 清屏/背景色，默认中灰 |
| `AmbientColor` | `Vector3` | 环境光（RGB 强度 0-1），默认较暗 |
| `ShowGrid` / `ShowWorldAxes` | `bool` | 是否显示默认网格/世界坐标轴 |
| `WorldAxesLength` | `float` | 世界坐标轴长度（默认 1.0） |

方法 / Methods:
- `void Add(GameObject go)` / `bool Remove(GameObject go)` / `void Clear()`
- `GameObject Find(string name)` —— 按名查找（深度优先）
- `IReadOnlyList<GameObject> FindAll(string name)` —— 按名查找全部
- `IEnumerable<GameObject> EnumerateAll()` —— 全树枚举
- `Update(double deltaTime)` —— 递归调用各节点 `Update` + 相机/模型更新（更新线程）
- 拾取：`bool Pick(Ray ray, out RaycastHit hit, Func<GameObject,bool>? predicate = null)`、`bool PickAndHighlight(Ray ray, Color? color = null, ...)`、`ClearHighlight()`
- `SetWorldAxes(bool visible, float length)` / `ShowGrid(bool visible)`

### `Camera`
「显示状态对象」，不持有场景，仅数学状态。可通过 `SceneGraph.Camera` 整体替换。

属性：`Position`, `Target`, `Up`, `NearPlane`, `FarPlane`, `Fov`, `Orthographic`, `OrbitDistance`, `ClipFarAllowed` 等。
方法：
- `Vector3 Forward` / `Vector3 Right` / `Vector3 Up` / `Vector3 Eye`（unit 向量）
- `Matrix4x4 GetViewMatrix()` / `Matrix4x4 GetProjectionMatrix(float aspect)` / `GetViewProjection(aspect)`
- 手势：`Rotate(float pitchDelta, float yawDelta, float distanceDelta = 0)`、`Pan(Vector2 delta)`、`Zoom(...)`、`Reset()`
- `Ray ScreenToWorldRay(Vector2 ndc)` —— 拾取入口；`Vector3 ScreenToWorld(Vector3 ndc)`

### `Light`
`enum LightType { Directional, Point }`。成员：`Type`, `Color`（`Vector3` 强度）、`Direction` / `Position`、`Intensity`、`Range`。暴露 `WorldPosition` / `WorldDirection`（渲染用）。

### `GameObject` 图元（`Geometry/`）
均为 `GameObject` 派生，构造即生成 CPU 网格与材质，可直接 `scene.Add`：
- `Box(width, height, depth, name?)` / `Sphere(radius, name?)` / `Cylinder(radius, height, name?)`（轴沿 +Z）/ `Capsule(radius, height, name?)`（轴沿 +Z，总高 = height + 2×radius）
- `GroundPlane(size, name?)` / `Arrow(...)`（方向箭头）/ `Axes(length, name?)` / `Grid(size, spacing, name?)` / `Curve(...)` / `PointCloud(...)`

---

## 2. Geometry / 几何与数据 (`Geometry/`)

### 射线与边界
- `RaycastHit`：`Hit`(bool)、`Distance`、`Point`、`Normal`、`GameObject`。
- `Ray` 属于 `Camera`/`ScreenToWorldRay` 的输出；`SceneGraph.Pick*` 接收 `Ray`。

### `Raycast`（静态）
- `bool HitBounds(Bounds bounds, Ray ray, out float distance)`
- `bool HitMesh(MeshData mesh, Ray ray, out float distance, out Vector3 normal, out float u, out float v)` —— 已变换后的网格
- `bool HitSphere(Vector3 center, float radius, Ray ray, out float distance, out Vector3 normal)` —— 保留为通用工具
- `bool HitPlane(Vector3 point, Vector3 normal, Ray ray, out float distance)` —— 保留为通用工具
- `bool IntersectTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 normal, out float u, out float v)` —— Möller-Trumbore，双面命中

### `Bounds`
- `Bounds(Vector3 min, Vector3 max)`、`CreateFromPoints(IEnumerable<Vector3>)`、`static Bounds FromPoints(...)`。
- `Min` / `Max` / `Center` / `Extents` / `Radius`，`Contains(Vector3)`，`Encapsulate(Vector3)` / `Encapsulate(Bounds)`，`IsEmpty`。

### 点云 `PointCloud2Data` / `PointField` / `PointFieldDataType`
ROS `sensor_msgs/PointCloud2` 风格：一个扁平字节缓冲 + 字段描述。每个点占 `PointStep` 字节，各通道（x/y/z/rgb/intensity）由 `PointField`（offset + datatype + count）定位。位置通道（x/y/z）必需；颜色可选，按 rgb/rgba → 单独 r/g/b → intensity（灰度）的优先级解析。

| 成员 | 说明 |
|---|---|
| `FrameId` | 可选来源/坐标系标识 |
| `Fields` / `PointStep` | 字段描述 / 每点字节数 |
| `Width` / `Height` / `Count` / `IsOrganized` | 组织信息 / 点数 / 是否组织化（Height>1） |
| `HasColor` | 是否携带可解析颜色通道 |

方法：构造 `(PointField[] fields, byte[] data, int pointStep, string? frameId = null, int width = 0, int height = 1)`、`float[] ToPositionArray()`、`float[]? ToColorArray()`（无颜色通道返 null，由渲染器用材质统一色）、静态 `FromPositions(IEnumerable<Vector3>, frameId?)`。

- `PointField`: `Name` / `Offset` / `DataType` / `Count`；静态 `GetElementSize(type)`；`Size`（每点字节 footprint）。
- `PointFieldDataType`: `Int8/UInt8/Int16/UInt16/Int32/UInt32/Float32/Float64`（数值与 ROS datatype 一致）。

### `PointCloudIo`
从文件加载点云：静态 `Load(string path, string? frameId = null)` 按扩展名（`.pcd` / `.ply`）选解析器；`ReadPcd` / `ParsePcd` / `ReadPly` / `ParsePly`。`binary_compressed` 不支持并抛 `NotSupportedException`。

### 模型导入 `Import/`
- `AssimpModelLoader`（静态）：`LoadedModel Load(string filePath, LoadOptions? options = null)`。使用 Assimp；管线含三角化、自动 UV/法线/切线、顶点合并、校验与缓存友好排序。
- `LoadOptions`：`Default`、`FlipUvV`、`FlipWinding`、`GlobalScale`（unit 修正；URDF 网格 scale 属 Transform，不在此烘焙）。
- `LoadedModel`：`FilePath`、`Meshes`（`IReadOnlyList<LoadedMesh>`）。`LoadedMesh`：`Name`、`MeshData`、`MaterialData`。
- `AssimpNative`（静态）：`bool EnsureRuntime()` —— Linux 下为 Assimp 原生库建立 libdl 兼容链接（由宿主/工具组合根显式触发，库不自动运行）。

---

## 3. Rendering / 渲染抽象与数据

### 接口 / Interfaces
- `IRenderContext : IDisposable`：`event Action<int,int>? Resized`、`void Resize(int width, int height)`、`void Clear(Vector4 clearColor, bool clearDepth = true)`。
- `IRenderer : IDisposable`：`void Render(SceneGraph scene)`（必须只从渲染线程调用）。

### `MaterialData`
材质描述（CPU 数据），无 GPU/着色器状态——具体 GPU 材质实例化与贴图上传由渲染后端在渲染时完成。

| 成员 | 说明 |
|---|---|
| `BaseColor` | RGBA 基色，分量 [0,1] |
| `MetallicFactor` / `RoughnessFactor` | 金属度/粗糙度 [0,1] |
| `AlbedoTexture` / `NormalTexture` / `MetallicTexture` / `RoughnessTexture` | `TextureReference?` 各纹理槽 |
| `DoubleSided` | 是否双面渲染（关闭背面剔除） |
| `Wireframe` | 是否线框 |
| `PassKind` | `RenderPassKind`，默认 `Model` |

### `TextureReference`（`record`）与 `TextureColorSpace`
CPU 侧贴图引用，只描述「哪个文件、何种语义」，不持有 GPU 状态。

- `record TextureReference(string? FilePath = null, byte[]? ImageData = null, TextureColorSpace ColorSpace = TextureColorSpace.Srgb, bool GenerateMipmaps = true)`
- `bool IsValid`（`HasFileData || HasMemoryData`）、`HasFileData`、`HasMemoryData`
- `static FromFile(string filePath, TextureColorSpace, bool)` 、 `static FromData(byte[] data, TextureColorSpace, bool)`
- `enum TextureColorSpace { Srgb, Linear }`

### `RenderPassKind`
`Model=0, Line=1, Point=2, Skybox=3, Axes=4`。`Core` 只定义「哪些通道存在」，各通道的 GLSL 实现由后端 `EmbeddedShaders` 提供。

---

## 4. Utils / 工具

| 类型 | 说明 |
|---|---|
| `GameTimer` | 后台线程固定帧率滴答器：`TargetFps`（默认 60）、`Start/Stop`、`Tick(Action<float>)`、`Dispose` |
| `Logger` | 进程级静默日志门面（`Microsoft.Extensions.Logging` 薄封装）：`Initialize(Action<ILoggingBuilder>?)`、`Shutdown`、`OnLog`、`MinimumLevel`、`IsEnabled`、`UiContext`、`Trace/Debug/Info/Warning/Error/Critical`、`GetLogger` |
| `MathUtils` | 度数/弧度换算（`DegreesToRadians`、`RadiansToDegrees`）、`Clamp`、`Lerp`、`NormalizeAngle`、常量 `Pi`/`TwoPi`/`DegToRad`/`RadToDeg` |

---

## 5. 反例 / Anti-patterns

| 反例 | 原因 |
|---|---|
| public 返回/接收 `GL`/`Mesh`/`ShaderProgram`/`GraphicsContext` | 破坏跨后端/跨 UI 可移植性 |
| public 字段暴露内部 `Transform` 直接改写（作为正规用法） | 绕过只读保护，线程/所有权不清 |
| 把 GL/UI 类型放进数据模型 | 模型必须可序列化、可 headless |
| 库内实现点选菜单/属性面板等应用交互 | 交互属宿主 UI 框架职责 |
