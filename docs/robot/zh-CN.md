# RobotSimulation.Robot 公共 API（简体中文）

> 状态：反映当前实际代码。命名空间：`RobotSimulation.Robot.*` · 依赖：`RobotSimulation.Core`。
> 配套：`../architecture/zh-CN.md`、`../core/zh-CN.md`、`../opengl/zh-CN.md`。

`Robot` 是机器人领域模型：把 URDF / 任意描述解析为「纯数据描述」，并提供两条消费路径——① `RobotModel`（一棵 `GameObject` 树，供可视化），② `RobotState`（headless 关节状态 + 正向运动学 FK）。`Core` 永不引用 `Robot`；`Robot` 引用 `Core` 仅用于 `MeshData` / `MaterialData` / `Scene` 等 CPU 数据与 `Logger`。

---

## 1. `RobotModel`（机器人 = 一棵 GameObject 树）

`RobotModel : GameObject`。它就是机器人树的根（根 link 的骨架节点即它本身），是 URDF/描述的唯一入口：读一个 URDF 或从任意 `RobotDescription` 构造，立刻得到一棵可驱动、可扩展的机器人 `GameObject` 树，可 `scene.Add` 后由渲染器遍历绘制。节点只持有 CPU 数据（`MeshData` / `MaterialData`），不含渲染参数。

### 构造与工厂

```csharp
// URDF 文本 → 完整机器人树
public static RobotModel Parse(string urdfXml, string? baseDirectory = null,
    IAssetResolver? resolver = null, string? assetDirectory = null);

// 读取 URDF 文件 → 完整机器人树（文件目录自动作为相对资源路径基准）
public static RobotModel ParseFile(string path, IAssetResolver? resolver = null,
    string? assetDirectory = null);

// 扩展点：任意描述源（SDF/自定义）→ 同一棵机器人树
public RobotModel(RobotDescription description, IAssetResolver? resolver = null,
    string? assetDirectory = null);
```

`assetDirectory` 是可选资源根（MuJoCo `meshdir` 的等价物）：mesh/贴图先在该目录下找，找不到再回退到 URDF 同级目录。**它是加载「标准 ROS 布局」的关键**——当 URDF 放在 `包根/urdf/` 而资源在 `包根/meshes/` 时，`assetDirectory: 包根` 即可，无需改动 URDF。省略（默认）＝只在 URDF 同级目录找。

> `assetDirectory` 与自定义 `resolver` **互斥**：查找归 resolver 管，同时给两者只会让其中一个被静默忽略，因此构造时直接抛 `ArgumentException`。

要求 `description` 恰好只有一个根 link；多根并非错误（「部分装配」），但构建为单个 `GameObject` 需要单根。

### 属性

| 成员 | 类型 | 说明 |
|---|---|---|
| `Description` | `RobotDescription` | 构建此树的纯数据描述（headless / 序列化 / 多实例复用） |
| `RobotName` | `string` | URDF `<robot name>`；与 `GameObject.Name`（根 link 名）不同 |
| `AssetResolver` | `IAssetResolver?` | 调用方传入的解析器；为 `null` 表示由默认 `FileSystemAssetResolver` 查找（它自身绑定工厂收到的 `assetDirectory`） |
| `DrivableJoints` | `IReadOnlyList<Joint>` | 可驱动关节（Revolute/Continuous/Prismatic，按描述顺序） |
| `DrivableJointCount` | `int` | 可驱动关节数（`ApplyJointValues` 所需数组长度） |
| `RootPose` | `Matrix4x4` | 机器人整体（根 link）在父坐标系位姿；经由内置接口写入 |

### 方法

```csharp
public RobotState CreateState();                                  // headless 关节状态 + FK
public void SetJointValue(string name, float value);              // 按名驱动（旋转弧度/平移米）并即时更新对应子 link 的本地 Transform
public void ApplyJointValues(IReadOnlyList<float> values);         // 按可驱动顺序批量驱动
```

> 关节只作用于 `Transform`（纯数据），从不触碰 OpenGL。构建完成后整棵子树被 `Transform` 只读锁定；子 link 位姿只能经 `RobotModel` 内置接口或 `RootPose` 写入，禁止外部直接改写。

### 使用示例

```csharp
using RobotSimulation.Robot;

var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);                       // 与其它 GameObject 平等加入

robot.SetJointValue("shoulder", 1.2f);  // revolute: 弧度
robot.SetJointValue("slider", 0.35f);   // prismatic: 米
robot.RootPose = Matrix4x4.CreateTranslation(new Vector3(1, 0, 0));

// headless FK 状态对象（不依附场景，可用多实例）
RobotState state = robot.CreateState();
state.SetJointValue("shoulder", 1.2f);
Matrix4x4 ee = state.GetLinkGlobalPose("tool0");
```

---

## 2. 描述数据模型 / Description (`Description/`)

> 命名空间 `RobotSimulation.Robot.Description`。纯数据、零渲染依赖、可序列化；只覆盖可视化相关信息，忽略物理参数（inertial、collision 等）。

- `RobotDescription`：`Name`（`<robot name>`）、`RootLink`、`Links` / `Joints`（`IReadOnlyList`）。
- `Link`：`Name`、`Visual`（根视觉）或 `Visuals`（多视觉）、可选 `Inertial`（未用）。
- `VisualElement`：`Name`、`Geometry`、`MaterialName`、`LocalTransform`（行主序 `Matrix4x4`）、可选 `Visible`。
- `Joint`：`Name`、`Type`、`ParentLink`、`ChildLink`、`Origin`（`Matrix4x4`）、`Axis`、`Limits`、`Mimic`。
  - `JointType`: `Revolute/Continuous/Prismatic/Fixed/Planar/Floating`。
  - joint 即「给定关节值 → 子 link 相对父 link 的变换」。revolute/continuous 绕 `Axis` 转角，prismatic 沿 `Axis` 平移，fixed 无自由度。
- `Geometry` 基类：
  - `BoxGeometry(Vector3 Size)`
  - `SphereGeometry(float Radius)`
  - `CylinderGeometry(float Radius, float Length)` —— 轴沿 +Z
  - `CapsuleGeometry(float Radius, float Length)` —— 轴沿 +Z；总高 = Length + 2×Radius
  - `MeshGeometry(string Uri)` —— 仅记录原始引用字符串（相对路径 / `package://`），不在此层解析文件

---

## 3. URDF / (`Urdf/`)

- `UrdfParser`（`internal`）：`RobotDescription Parse(XDocument document, string? baseDirectory)`。仅处理可视化：robot/link/visual/geometry/material/joint（拓扑）；显式忽略 inertial/collision/transmission/gazebo；位姿统一为行主序矩阵；数值以 `InvariantCulture` 解析。用户经 `RobotModel.Parse/ParseFile` 进入。
- `UrdfParseException`（`public`）：解析失败时抛出，消息含元素/属性上下文以帮助定位文件。

### `IAssetResolver` / `FileSystemAssetResolver`
资产定位扩展点：把 URDF 中的 mesh/texture 引用字符串解析为可访问绝对路径。

```csharp
public interface IAssetResolver { string? Resolve(string uri, string? baseDirectory); }

public sealed class FileSystemAssetResolver : IAssetResolver
{
    public string? AssetDirectory { get; }                        // 额外资源根（绝对化后保存），未配置为 null
    public FileSystemAssetResolver(string? assetDirectory = null);
}
```

默认 `FileSystemAssetResolver` 的查找规则（**回退链，命中即止**）：

1. **绝对路径** → 原样使用；
2. **`package://<包名>/<rest>`** → **丢掉包名**，只把 `<rest>` 当作相对路径继续（见下）。包名**从不**用来匹配目录名：它是 ROS 包标识，URDF 里写 `package://rus_sim_driver/…` 而文件实际放在 `fairino3_v6/` 是常态（本仓库自带模型就是一例），包名在磁盘上不携带可用信息；
3. **相对路径** → 依次尝试两个根，先命中者胜：
   - `AssetDirectory`（构造时指定，即 MuJoCo `meshdir` 的等价物）—— **优先**；
   - `baseDirectory`（URDF 文件自身所在目录）—— **默认**；
   - 两者都没有（`Parse` 未给 `baseDirectory` 且未配 `AssetDirectory`）→ 退回当前工作目录；
4. **每个根都不存在** → 抛 `FileNotFoundException`，消息中**列出所有尝试过的路径**并提示配置 `AssetDirectory`；绝不静默跳过。

```csharp
// 默认：资源与 URDF 同级（本仓库测试数据的布局，无需任何额外参数）
var robot = RobotModel.ParseFile("Assets/Models/fairino3_v6/fairino3_v6.urdf");

// 标准 ROS 布局：包根/{urdf/robot.urdf, meshes/…} —— 用 assetDirectory 指向包根即可
var robot2 = RobotModel.ParseFile("pkg/urdf/robot.urdf", assetDirectory: "pkg");
```

---

## 4. State / headless 正向运动学 (`State/`)

`RobotState` 在 `RobotDescription` 之上维护可驱动关节值，并计算正向运动学（FK）。纯计算，无渲染/GL/UI 依赖，可 headless 使用（轨迹、物理、可视化可共享同一状态源）。

| 成员 | 类型 | 说明 |
|---|---|---|
| `Description` | `RobotDescription` | 构造时使用的静态描述 |
| `DrivableJoints` | `IReadOnlyList<Joint>` | 可驱动关节（Revolute/Continuous/Prismatic，按描述顺序） |
| `DrivableJointCount` | `int` | 可驱动关节数 |
| `RootPose` | `Matrix4x4` | 机器人根在世界的位姿（默认 `Identity`），所有 FK 基于它 |

方法:
- `void SetJointValue(string name, float value)` —— 按名驱动（旋转弧度/平移米）
- `float GetJointValue(string name)` —— 读取某可驱动关节当前值
- `void ApplyJointValues(IReadOnlyList<float> values)` —— 批量赋值（长度须等于 `DrivableJointCount`）
- `Matrix4x4 GetLinkGlobalPose(string linkName)` —— 某 link 相对 `RootPose` 的全局位姿（先更新 FK 再查询）
- `bool TryGetLinkGlobalPose(string linkName, out Matrix4x4 pose)` —— Try 版本

> 位姿为行主序矩阵（与 `Core.Scene.Transform` 一致），`GetLinkGlobalPose` 结果可直接写入 `GameObject.Transform` 或被他系统消费。非线程安全：假定单线程使用；上层如跨线程推送需先对状态做快照。

```csharp
using RobotSimulation.Robot;

var state = RobotModel.Parse(File.ReadAllText("robot.urdf")).CreateState();
state.SetJointValue("shoulder", 1.2f);
state.SetJointValue("slider", 0.35f);
Matrix4x4 ee = state.GetLinkGlobalPose("tool0");
```

---

## 5. 关键解析行为

1. 数值一律 `CultureInfo.InvariantCulture`（URDF 小数点恒为 `.`）。
2. 材质两遍收集：先收集 robot 级与 link 内嵌的 `material name→定义`，再解析 visual 对 name 的引用；同名后者覆盖；缺失记录警告并置空（渲染用默认色）。
3. `rpy` → 位姿矩阵：URDF 为固定轴 XYZ（R = Rz(yaw)·Ry(pitch)·Rx(roll)），合成 `Matrix4x4` 存入 `Joint.Origin` / `VisualElement.LocalTransform`（行主序，与 `Core.Scene.Transform` 一致）。
4. 拓扑校验：link/joint 重名、引用 link 不存在、parent 回溯成环等错误上报；多根合法（「部分装配」）不报错。
5. 几何语义：cylinder/capsule 轴沿 +Z；尺寸即 URDF size/radius/length，不做引擎侧缩放。
6. Mesh 引用不在此层解析文件：`MeshGeometry.Uri` 仅存原始字符串，由 `Core/Geometry/Import/AssimpModelLoader` 与 `IAssetResolver` 消费，产物是 `MeshData`（经 `RobotModel` 挂到 visual 节点）。

---

## 6. 可见性

- **public（用户面）**: `RobotModel`、`IAssetResolver`、`FileSystemAssetResolver`（含 `AssetDirectory` 属性与构造参数——资源根入口）、`UrdfParseException`、全部 `Description` 数据模型、`RobotState`。
- **internal（实现细节）**: `UrdfParser`——用户只通过 `RobotModel.Parse/ParseFile` 进入。
- Robot 层不再出现任何渲染/GL 资源；`RobotModel` 构建只产出 CPU 数据（`MeshData`/`MaterialData`），GPU 实例化统一由渲染后端在渲染线程完成。
