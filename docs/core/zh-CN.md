# RobotSimulation.Core 公共 API（简体中文）

> 状态：反映当前实际代码。命名空间：`RobotSimulation.Core.*` · 依赖：`Silk.NET.Assimp`、`Microsoft.Extensions.Logging`。
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
| `ShowLocalAxes` | `bool` | 是否挂载本节点局部坐标轴（自动子对象）。挂载出来的轴系带 `AlwaysOnTop`：这条标记所标注的节点，网格本身就在它四周，所以它在场景画完之后绘制，任何几何都埋不掉它 |
| `LocalAxesLength` | `float` | 局部坐标轴默认长度，默认 0.3 |
| `LocalAxes` | `Axes?` | 已挂载的局部坐标轴（非 null 当启用后；要微调就改这里——把 `AlwaysOnTop` 设回 false 又得到普通、会被遮挡的轴系） |
| `MaterialData` | `MaterialData?` | 材质描述（CPU）；`null` 表示不绘制/默认外观 |
| `Visible` | `bool` | 是否参与画面，默认 true。是**子树开关**：关闭即隐藏该节点**及其全部后代**（渲染器不再往下走），且同一子树默认也退出射线拾取（要拾取它得用 `Pick(..., hitInvisible: true)`） |
| `Highlighted` | `bool` | 是否高亮（默认 false），用于选择反馈。该染色由 Model 通道施加；line/point/axes 通道不带光照、会忽略它（这些恰好也都是不参与拾取的显示辅助物） |
| `HighlightColor` | `Vector4` | 高亮混合色，默认橙黄 |
| `Pickable` | `bool` | 是否参与射线拾取，默认 true |
| `UpdateBehaviors` | `IReadOnlyList<IUpdateBehavior>` | 已挂载的更新行为（按执行顺序；无则空） |
| `UpdateBehaviorCount` | `int` | 已挂载更新行为数量（0 = 每帧无逻辑） |

方法 / Methods:
- 构造 `GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")`
- `void SetSubtreePickable(bool value)` —— 递归设置本节点及后代的 `Pickable`
- `void LoadModel(string filePath, LoadOptions? options = null)` —— 从模型文件（STL/OBJ/DAE/glTF）加载几何+材质到本节点（仅支持单 submesh；多 submesh 用 `AssimpModelLoader.Load`）
- `void Update(double deltaTime)` —— 每帧派发：按挂载顺序调用 `Enabled` 的行为（由 `SceneGraph.Update` 父先子后调用）
- `T AddBehavior<T>(T behavior) where T : IUpdateBehavior` —— 挂载一个行为并原样返回（便于后续卸载/暂停）
- `DelegateUpdateBehavior AddUpdate(Action<GameObject, double> update)` —— 用 lambda 注入每帧逻辑（收到「节点, dt」）
- `DelegateUpdateBehavior AddUpdate(Action<double> update)` —— 同上，不需要节点时使用
- `bool RemoveBehavior(IUpdateBehavior behavior)` —— 卸载行为，返回是否曾挂载
- `void ClearBehaviors()` —— 清空全部更新行为

#### 更新行为（组合优于继承）

节点不再靠「继承 + 重写 `Update`」扩展，而是**携带**逻辑：把任意多个 `IUpdateBehavior` 挂到节点上，`SceneGraph.Update` 每帧按挂载顺序、父先子后依次派发。逻辑在运行时装配，可增删、可暂停、可单测，且对 `sealed` 或由文件解析产出的节点同样适用（无需子类）。行为运行在更新线程，只可改 CPU 数据，绝不碰 GL。

| 类型 | 位置 | 说明 |
|---|---|---|
| `IUpdateBehavior` | `Core.Scene` | 契约：`bool Enabled { get; set; }` + `void Update(GameObject owner, double deltaTime)`；列表里装的就是它 |
| `DelegateUpdateBehavior` | `Core.Scene` | 把 lambda 适配成 `IUpdateBehavior` 的语法糖，由 `AddUpdate` 创建并返回 |

```csharp
var box = new Box(new MeshData(), new MaterialData());
scene.Add(box);

float spin = 0;
box.AddUpdate((go, dt) =>                       // 逻辑 = 一行 lambda，无需子类
{
    spin += (float)(dt * Math.PI);
    go.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin);
});
```

- 同一节点可挂多套互不相关的逻辑（各一个 lambda）；`RemoveBehavior` / `ClearBehaviors` 卸载，同一实例可再次挂载。
- 暂停而不卸载：把 `AddUpdate` 返回的 `DelegateUpdateBehavior.Enabled` 置 `false`，恢复即置 `true`。
- 复杂/可复用的逻辑可自建类实现 `IUpdateBehavior`，再用 `AddBehavior<T>` 挂载；当前**未**提供行为基类，按需再补。
- 未挂任何行为的节点零开销：行为列表惰性创建，`Update` 仅做一次空判断。

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
场景根容器：持有对象树、活动相机、灯光集合与场景显示默认值（背景/环境光/网格地面）。构造函数自动装配默认相机姿态、默认灯光与网格地面；世界坐标轴只在 `ShowWorldAxes` 打开时创建（默认关闭——「X/Y/Z 朝哪」由渲染器绘制的屏幕空间朝向 gizmo 承担，不必往场景里放一套会被模型遮挡、还会随缩放变形的坐标轴）。选择则是把同一个问题问到**某一个节点**上：`Select` 高亮选中的对象、并在它身上挂一套它自己的局部坐标轴（画在几何之上，见 `Axes.AlwaysOnTop`）——「选中了什么」与「它朝哪」一次一起给出。这套坐标轴是**纯显示**的：它只是一个普通子节点，没有任何拖拽手柄，场景也从不回写节点位姿——所以「看参考系」永远碰不到由关节链决定的姿态。

| 成员 | 类型 | 说明 |
|---|---|---|
| `Roots` | `IReadOnlyList<GameObject>` | 根节点（渲染遍历与外部只读访问） |
| `Camera` | `Camera` | 活动相机（可整体替换） |
| `BackgroundColor` | `Vector4` | 清屏/背景色，默认中灰 |
| `AmbientColor` | `Vector3` | 环境光（RGB 强度 0-1），默认较暗 |
| `ShowGrid` | `bool` | 是否显示默认网格地面（默认 true） |
| `ShowWorldAxes` | `bool` | 是否显示默认世界原点坐标轴（默认 **false**：需要世界尺度参照时再打开） |
| `WorldAxesLength` | `float` | 世界坐标轴长度（米，默认 0.5）。默认坐标系是 `AxesSizing.FixedWorldLength`，所以这是真实长度而非屏幕尺寸 |
| `ShowOrientationGizmo` | `bool` | 是否绘制屏幕空间朝向 gizmo（默认 true） |
| `OrientationGizmoMargin` | `float` | gizmo 到视口右下边缘的间距（像素，默认 16）。该控件只有三支箭头与一个中心小球，不带背景底盘；它的**尺寸**归渲染器管、不是场景设置——按视口较短边的固定比例计算（`Renderer.GizmoSizeRatio`），所以窗口大小变化时标记的视觉权重保持一致（原先固定像素的 `OrientationGizmoSize` 已移除） |
| `ShowSelectionAxes` | `bool` | 是否在选中对象上显示它自己的局部坐标轴（默认 true；纯显示——是标记，不是拖拽手柄）。挂载时带 `AlwaysOnTop`，所以在被标注的那块网格内部照样看得清 |
| `SelectionAxesLength` | `float` | 选中对象所挂坐标轴的参考长度（米，默认 0.3） |
| `Selected` | `GameObject?` | 当前选中的对象（只读；请经 `Select` / `PickAndSelect` 改变选择） |
| `GridCellSize` / `GridCellCount` / `GridColor` | `float` / `int` / `Vector4` | 网格地板参数：格边长（米）/ 单侧格数 / 线色 |
| `Lights` | `IReadOnlyList<Light>` | 场景灯光（`Add` 自动收集） |
| `Version` | `long` | 已应用的结构变更计数（单调、可无锁读取）：自己从 `Roots` 派生出来的缓存可以据此失效，只有「它变了」有意义，起始值无意义 |
| `PendingChangeCount` | `int` | 其它线程已排队、尚未应用的变更数（诊断用：帧边界没在跑时它会一直涨） |
| `MaxPendingChanges` | `int` | 生产者队列的上限（默认 `DefaultMaxPendingChanges` = 65536，0 = 不限）：超过后丢弃后续变更并记一次警告。它不是性能旋钮，而是让误用可见——以数据频率入队结构变更时，队列会一直涨到内存为止，那比一行警告难查得多 |
| `DroppedPendingChangeCount` | `long` | 因超过 `MaxPendingChanges` 而被丢弃的变更数（诊断用；健康时恒为 0） |
| `IsOwnerThread` | `bool` | 调用线程是否场景属主线程——允许遍历 `Roots` / `Lights`、允许跑 `Update` / `ApplyPendingChanges`，也是 `Add` / `Remove` 立即生效的那个线程 |

方法 / Methods:
- `void Add(GameObject? obj)` —— 加入场景（根节点自动登记；`Light` 同时进 `Lights`）。**任何线程可调**：在场景属主线程（构造线程，或宿主在开跑前用 `ClaimOwnership()` 显式交接过去的帧循环线程）上立即生效，在其它线程上入队、于下一个帧边界生效——这正是「一边渲染一边往场景里加对象」安全的原因（队列把"结构变更"收拢到帧边界，渲染遍历永远不会撞上正在被改的列表）。生效后的对象与一开始就在场景里的对象完全一样：渲染器是**惰性**创建 GPU 资源的（首帧见到才建），且每帧都重新遍历 `Roots`。带父节点的对象不是根：给它 `Add` 不会重复登记（它由父节点抵达），而一个根节点之后获得父节点时会从 `Roots` 里退出——所以同一个节点不会既在 `Roots` 又在父节点下面
- `void Remove(GameObject? obj)` —— 移出场景（`Light` 同时出 `Lights`）；线程契约同 `Add`。根列表与父节点两处都会清（不是「看它的 `Parent` 指向哪就清哪」），所以**移除是幂等的**：不论节点当前处于什么状态，一次 `Remove` 就够，不存在「要删两次才消失」。被移出的节点不会留在选择里：移除选中节点、或移除它的某个祖先（整棵子树一起离开）都会一并撤掉选择的高亮与 `Select` 挂上的坐标轴
- `int ApplyPendingChanges()` —— **帧边界**：应用其它线程排队的结构变更（`Add` / `Remove` / 重挂），返回本次应用了几条。`Update` 与渲染器（`IRenderer.Render` 的实现）都会替宿主先调一次，所以常规帧循环无需自己调（一帧里的第二次调用发现队列为空会直接返回，连锁都不取）；自己驱动渲染链路的宿主可以自行调用。它必须在**属主线程**上调用，且**不再把调用线程认作属主**：非属主线程上调用会抛 `InvalidOperationException` 并指向 `ClaimOwnership()`——「允许遍历 `Roots` 的线程」和「允许写列表的线程」必须永远是同一个，静默改道会让上一个属主（可能正在遍历）变成外来者。`Dispose` 之后它只清空队列、不再应用——帧边界不为迟到的生产者抛异常
- `void ClaimOwnership()` —— 把场景显式交接给调用线程（**开跑之前**调用一次）：此后该线程是属主，`Add` / `Remove` 在它上面立即生效，`Update` / `ApplyPendingChanges` 也能在它上面跑。用于「在 A 处建场景、在 B 线程跑帧循环」的宿主；在帧循环里（或帧循环所在线程上）`new SceneGraph()` 的宿主永远不需要它。已经跑过帧边界之后再交接会抛异常（当前属主可能正在遍历列表，第二个属主正好会踩上去）；在属主线程上调用是空操作，所以可以防御性地调
- `Update(double deltaTime)` —— 先过帧边界（`ApplyPendingChanges`）再递归调用各节点 `Update`（只写 CPU 数据，绝不碰 GL）。因为 `Update` 与 `Render` 都是帧边界，二者必须在**同一线程**（宿主帧循环）上调用——也就是场景属主线程
- 树的重挂：`Transform.Parent` 赋值是唯一入口（`Transform.Children` 是只读视图）。节点已在场景里时，赋值走场景的帧边界：属主线程上立即生效并同步 `Roots` 成员关系，其它线程上入队、最迟下一帧可见；节点加入场景之前不属于任何场景，此时在任意线程上建树仍是立即生效（`Add` 之前先把子树搭好是推荐姿势）。赋一个会成环的父节点（自身或自己的后代）会抛 `ArgumentException`；若该变更来自别的线程、到帧边界时才被发现已经成环，则丢弃并记一次警告——不为一个「发出时合法」的请求把帧循环打下来
- 默认装配：`Grid? AddDefaultGrid()`、`void AddDefaultLights()`、`Axes? AddDefaultWorldAxes()`、`void ApplyDefaultCamera()`（构造函数已按 `ShowGrid` / `ShowWorldAxes` 调用前三个）—— 由此而来的顺序陷阱：构造函数**先于**对象初始化器执行，所以 `new SceneGraph { ShowGrid = false }` 为时已晚（网格已建好）。该标志只是 `AddDefaultGrid()` 的判断条件，构造后再关默认网格得把那个节点移除（`Remove`）
- `float FitWorldAxesToContent(float factor = 1.15f)` —— 按场景内容世界包围盒的最大边长 × `factor` 重设坐标轴长度（测量时忽略坐标系自身、网格与灯光），让轴「伸出模型之外」而不是藏在模型里；会同步已经创建的那一套并返回所用长度，按 `WorldAxesName` 查找节点
- 拾取：`RaycastHit? Pick(Ray ray, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)`、`GameObject? PickAndHighlight(Ray ray, bool enable = true, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)` —— 命中需要 `Visible` + `Pickable` 且有 `MeshData`；不可见节点会让**整棵子树**退出遍历（与绘制时的分组完全一致），而 `hitInvisible: true` 对整棵子树忽略可见性，供编辑器选中刚被隐藏的对象
- 选择：`GameObject? Select(GameObject? obj, bool highlight = true)` —— 带反馈的单选：新对象拿到高亮，并在 `ShowSelectionAxes` 打开时挂上它自己的局部坐标轴（不可拾取，所以不会吞掉下一次点击；又在几何之上，见 `AlwaysOnTop`，所以反过来也不会被网格吞掉）；旧对象两者一起失去，传 null 即清空。只有 `Select` 自己挂上的坐标轴才会被它卸下。该标记只**显示**参考系、从不修改它——库内任何地方都没有拖拽手柄，所以一次点击碰不到机器人由关节链决定的姿态
- `GameObject? PickAndSelect(Ray ray, Func<GameObject,bool>? predicate = null, bool hitInvisible = false)` —— `Pick` + `Select` 一次完成，即宿主点击链路的全部
- `void Dispose()` —— 清空节点与灯光引用（GPU 资源由渲染器释放）

### `Camera`
「显示状态对象」，不持有场景，仅数学状态。可通过 `SceneGraph.Camera` 整体替换。

属性：`Yaw`, `Pitch`（度，`Pitch` 钳制 [-89, 89] 防翻转）, `Distance`（钳制 [0.5, 200]）, `Target`（轨道中心）, `Position`（只读，由轨道状态导出，并作为 `ScreenToWorldRay` 的射线起点）, `Fov`, `AspectRatio`（宿主在每帧/resize 时写入）, `NearPlane`, `FarPlane`。
方法：
- `Matrix4x4 GetViewMatrix()` / `Matrix4x4 GetProjectionMatrix()` —— 投影用 `AspectRatio` 属性（没有 aspect 参数版本）
- 手势：`Rotate(float deltaYawDeg, float deltaPitchDeg)`（**先 yaw 后 pitch**）、`Pan(Vector2 screenDelta)`（屏幕像素位移，X 右为正 / Y 上为正）、`Zoom(float delta)`（正数靠近）、`Reset()`
- `Ray ScreenToWorldRay(Vector2 screenPositionPixels, Vector2 viewportSizePixels)` —— 拾取入口：屏幕像素（左上原点、X 右、Y 下）→ 世界射线。**两个参数都是像素**，且视口尺寸必须与渲染用的那块视口一致（像素→NDC 由二者比值决定），否则射线会偏

### `Light`
`enum LightType { Directional, Point }`。成员：`Type`, `Color`（`Vector3` 强度）、`Position` / `Direction`（与其他 `GameObject` 一致，都是节点自身坐标系下的值）、`Intensity`、`Range`。渲染器读的是世界坐标对 `WorldPosition` / `WorldDirection`（位置沿祖先链复合，方向经祖先链传递并重新归一化），所以挂在别的节点下的灯（机器人上的灯具）会从它真正所在的位置照亮；不可见的灯（`Visible = false`）完全不参与着色。`Range` 目前只是被携带、尚未被消费：标准着色还没有衰减项。

### `GameObject` 图元（`Scene/Primitives/`）
均为 `GameObject` 派生，构造即生成 CPU 网格与材质，可直接 `scene.Add`：
- `Box(width, height, depth, name?)` / `Sphere(radius, name?)` / `Cylinder(radius, height, name?)`（轴沿 +Z）/ `Capsule(radius, height, name?)`（轴沿 +Z，总高 = height + 2×radius）
- `GroundPlane(size, name?)` / `Arrow(...)`（方向箭头）/ `Axes(length, name?)` / `Grid(size, spacing, name?)` / `Curve(...)` / `PointCloud(...)`
  - `Axes` 有两种尺寸策略（`AxesSizing`）：`ConstantScreenSize`（默认，屏幕尺寸由 `ScreenScale`/`MinWorldLength`/`MaxWorldLength` 固定，适合做「节点标记」）与 `FixedWorldLength`（箭头长度恒为 `Length` 个世界单位，适合做「尺子」）。两种模式下 `Length` 都可写：坐标轴着色器按箭头自身长度做归一化，改长度不需要重建几何。
  - `AlwaysOnTop`（默认 **false**）是该轴系的深度策略。关闭时它就是普通几何，模型能遮挡它；打开时渲染器在普通遍历里把整套轴系排队、最后在刚清空深度缓冲的画面上绘制，于是任何几何都藏不住它——而各轴系之间仍互相正确遮挡（两套重叠时近的那套胜出）。`GameObject.ShowLocalAxes`（因而 `SceneGraph.Select`）会把它打开，因为「对象自己的坐标系」正立在被标注的那块网格内部；`FixedWorldLength` 的尺子则应当保持关闭——一把能穿透被测物体的尺子，会把场景尺寸说成假的。

---

## 2. Geometry / 几何与数据 (`Geometry/`)

### 射线与边界
- `Ray`：`Origin` + 单位 `Direction`（构造时自动归一化，零向量抛 `ArgumentException`），因此 `t` 就是世界单位距离、可跨对象比较。
- `RaycastHit`：`Object`(`GameObject`)、`Point`（世界命中点）、`Distance`（沿射线，世界单位）、`Normal`（世界法线，朝向射线）、`U` / `V`（命中三角形的重心坐标）。
- `Camera.ScreenToWorldRay` 产出 `Ray`，`SceneGraph.Pick*` 消费 `Ray`。

### `Raycast`（静态；可空返回值表示未命中）
- `float? HitSphere(in Ray ray, Vector3 center, float radius)`
- `float? HitPlane(in Ray ray, Vector3 point, Vector3 normal)` —— 平行或位于射线背后返回 null
- `float? HitAABB(in Ray ray, in Bounds bounds)` —— slab 法宽相；起点在盒内返回负距离（仍算命中）
- `bool HitTriangle(in Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 normal, out float u, out float v)` —— Möller–Trumbore，双面命中（背面命中时法线翻向射线）

### `Bounds`
- 构造 `Bounds(Vector3 min, Vector3 max)`（不校验，调用方保证 `min <= max`）、`static Bounds FromPoints(IEnumerable<Vector3>)`（空集合得到 `Min = Max = 0` 的空盒）。
- 属性 `Min` / `Max` / `Center` / `Size`。

### 点云 `PointCloud2Data` / `PointField` / `PointFieldDataType`
ROS `sensor_msgs/PointCloud2` 风格：一个扁平字节缓冲 + 字段描述。每个点占 `PointStep` 字节，各通道（x/y/z/rgb/intensity）由 `PointField`（offset + datatype + count）定位。位置通道（x/y/z）必需；颜色可选，按 rgb/rgba → 单独 r/g/b → intensity（灰度）的优先级解析。

| 成员 | 说明 |
|---|---|
| `FrameId` | 可选来源/坐标系标识 |
| `Fields` / `PointStep` | 字段描述 / 每点字节数 |
| `Width` / `Height` / `Count` / `IsOrganized` | 组织信息 / 点数 / 是否组织化（Height>1） |
| `Capacity` / `FirstSlot` | 槽位总数 / 逻辑 0 号点（最旧）所在物理槽位 |
| `Revision` / `Dirty` | 内容修订号 / 自上次 `ResetDirty` 以来累积的变更窗口（`DirtyWindow`） |
| `HasColor` | 是否携带可解析颜色通道 |

方法：构造 `(PointField[] fields, byte[] data, int pointStep, string? frameId = null, int width = 0, int height = 1)`、`float[] ToPositionArray()`、`float[]? ToColorArray()`（无颜色通道返 null，由渲染器用材质统一色）、静态 `FromPositions(IEnumerable<Vector3>, frameId?)`（包装已有缓冲，整块即全部点）、静态 `CreateMutable(int initialCapacity = 0, bool withColor = false, string? frameId = null)`（容量已预留但点数为 0 的可增长点云，`withColor` 时布局带 r/g/b float 通道）。

**增量更新**（面向传感器流/实时建图）。写入：`AddPoint(p)`、`AddPoint(p, color)`、`AddPoints(ReadOnlySpan<Vector3>)`、`AddPoints(positions, colors)`、`AppendRawPoint(ReadOnlySpan<byte>)`（整点字节原样写入）、`SetPoint(i, p)`、`SetColor(i, c)`；删除：`RemoveOldest(n)`、`TrimTo(max)`、`Clear()`；容量：`EnsureCapacity(n)`。追加写在写游标处、驱逐只前移 `FirstSlot`——已有点的数据既不搬动也不重传。

- **逻辑索引** 0 = 最旧、`Count-1` = 最新，公开 API 均用逻辑索引；**物理槽位**由 `FirstSlot` / `Capacity` 推出，仅供后端上传：`GetSlotRuns(start, count, runs)` 把逻辑区间拆成至多两段物理区间，`CopySlotPositions` / `CopySlotColors` 按槽位区间导出（`CopyPositionsTo` / `CopyColorsTo` / `ToPositionArray` 按逻辑顺序导出）。
- **变更窗口**：`Revision` 每次内容变化自增；`Dirty` 给出逻辑区间 `[Start, Start+Count)` 与窗口起点修订号 `FromRevision`。后端若已上传到 `FromRevision`，只需上传该区间，否则全量；上传完成后调 `ResetDirty()` 推进窗口。驱逐不改变窗口起点（否则"先驱逐再追加"这种每帧序列会退化成全量重传），但会把窗口索引随点一起前移；容量增长会把环形拉直（`FirstSlot` 归 0）并把全部有效点标脏。
- **约束**：组织化点云（`Height > 1`）拒绝追加（抛 `InvalidOperationException`）；追加只写位置，其余通道填空值 0（`AddPoint(p, color)` / `AppendRawPoint` 可显式给值）；强度（intensity）灰度映射的 min/max 缓存会在写入/驱逐后失效，下一次取色重算。

- `PointField`: `Name` / `Offset` / `DataType` / `Count`；静态 `GetElementSize(type)`；`Size`（每点字节 footprint）。
- `PointFieldDataType`: `Int8/UInt8/Int16/UInt16/Int32/UInt32/Float32/Float64`（数值与 ROS datatype 一致）。

### `PointCloudIo`
从文件加载点云：静态 `Load(string path, string? frameId = null)` 按扩展名（`.pcd` / `.ply`）选解析器；`ReadPcd` / `ParsePcd` / `ReadPly` / `ParsePly`。`binary_compressed` 不支持并抛 `NotSupportedException`。

### 模型导入 `Import/`
- `AssimpModelLoader`（静态）：`LoadedModel Load(string filePath, LoadOptions? options = null)`。经 `Silk.NET.Assimp` 绑定使用 Assimp；管线含三角化、自动 UV/法线/切线、顶点合并、校验与缓存友好排序。
- `LoadOptions`：`Default`、`FlipUvV`、`FlipWinding`、`GlobalScale`（unit 修正；URDF 网格 scale 属 Transform，不在此烘焙）。
- `LoadedModel`：`FilePath`、`Meshes`（`IReadOnlyList<LoadedMesh>`）。`LoadedMesh`：`Name`、`MeshData`、`MaterialData`。
- 原生库：Assimp 二进制随 `Silk.NET.Assimp` 包按 RID 分发，宿主无需安装（早先基于 `AssimpNet` 的实现在 Linux 上需要 `libdl.so` 兼容链接，该补丁连同其引导类已随迁移删除）。但绑定本身只按裸名走操作系统搜索路径，找不到应用旁边的 `runtimes/<rid>/native` 副本，因此 `AssimpModelLoader` 在取函数表之前先按绝对路径映射这份副本 —— 宿主依旧无需任何准备。

---

## 3. Rendering / 渲染抽象与数据

### 接口 / Interfaces
- `IRenderContext : IDisposable`：`event Action<int,int>? Resized`、`void Resize(int width, int height)`、`void Clear(Vector4 clearColor, bool clearDepth = true)`、`GraphicsDeviceInfo DeviceInfo`。
- `IRenderer : IDisposable`：`void Render(SceneGraph scene)`（必须只从渲染线程调用）、`FrameStats Stats`。

### `FrameStats`（record struct）
渲染器上报的帧时序统计。纯数据（不含任何图形 API 类型），因此任何后端都能产出、任何宿主（Avalonia / WPF / 裸窗口）都能自绘 FPS 叠加层。

| 成员 | 说明 |
|---|---|
| `Fps` | 平滑后的帧率（基于两次渲染调用的间隔） |
| `LastFrameMilliseconds` | 最近一帧的墙钟耗时 |
| `AverageFrameMilliseconds` | 平滑后的平均帧耗时 |
| `FrameCount` | 自渲染器创建以来累计渲染帧数 |
| `Empty` | `static` 全零值（首帧之前） |

后端在每次渲染调用时更新（渲染线程），宿主只读取。

### `GraphicsDeviceInfo`（record struct）
用于诊断与支持的静态设备/驱动信息（「这台机器跑在什么显卡/驱动上？」）。纯数据（全部为字符串）。

| 成员 | 说明 |
|---|---|
| `Vendor` | 设备厂商字符串（等价 GL_VENDOR） |
| `Renderer` | 渲染器/显卡名（等价 GL_RENDERER） |
| `ApiVersion` | 图形 API 版本字符串（等价 GL_VERSION） |
| `ShaderVersion` | 着色语言版本字符串（等价 GL_SHADING_LANGUAGE_VERSION） |
| `Unknown` | `static` 空值（后端无法上报时） |

由后端从当前上下文填充；上层只用于显示或记录日志。

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
| 在非属主线程遍历 `SceneGraph.Roots` / `Lights`，或指望非属主线程的 `Add` 立刻出现在里面 | 结构变更在帧边界提交：遍历与读取属属主线程（渲染线程）；生产者只入队，生效时刻是下一个帧边界 |
| 把 `SceneGraph.Update` 与 `Renderer.Render` 放在两个线程上 | 二者都是帧边界（都要合入排队中的结构变更），分到两个线程等于让两次提交互相并发 |
