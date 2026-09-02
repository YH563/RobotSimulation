# Robot 模块文件架构（详细规划）

> 状态：规划稿 · 配套 `docs/architecture.md`（ADR-005/006/013/014）与 `docs/api.md`（§3.3）。
> 单工程内目录即命名空间边界；"阶段"列标注对应里程碑。

## 1. 依赖边界（本模块最重要的纪律）

```
RobotSimulation.Robot                  （单入口：RobotModel + RobotGameObject）
    │
    ├── Robot.Description  ──► 只引用 System.Numerics（零渲染、零 XML、零 GL）
    ├── Robot.Urdf         ──► 引用 Robot.Description + System.Xml.Linq
    ├── RobotModel.cs      ──► 引用 Robot.Description + Robot.Urdf + State +
    │                           Core.Geometry/Scene/Rendering（仅 CPU 数据资源，Instantiate）
    └── Robot.State        ──► 引用 Robot.Description（纯计算、零渲染）

Core 永不引用 Robot；Robot.Description/Urdf 永不引用 Core（ADR-006 切割线）。
```

## 2. 完整文件架构

```
Robot/                                   命名空间 RobotSimulation.Robot
│
├── RobotModel.cs                         命名空间 RobotSimulation.Robot          [M2✅/M3✅]
│      public sealed class RobotModel          ← 单入口：解析 + 创建（无独立 Loader/Builder）
│          static RobotModel Parse(string xml, string? baseDir = null, IAssetResolver? = null)
│          static RobotModel ParseFile(string path, IAssetResolver? = null)
│          RobotDescription Description { get; }        // ① 持有描述（headless/序列化/FK）
│          RobotState CreateState()                      //    便捷状态（headless）
│          RobotGameObject Instantiate()             // ② 创建场景对象（纯数据、无渲染参数）
│          // 内部：UrdfParser 解析 + 建树（link 骨架 / visual 子节点 / joint 挂接 / 驱动绑定）
├── RobotGameObject.cs                   命名空间 RobotSimulation.Robot          [M3 ✅]
│      public sealed class RobotGameObject : GameObject
│          // 与场景中任何 GameObject 平等：可 scene.Add / 挂任意父节点 / 统一释放
│          SetJointValue / ApplyJointValues / DrivableJoints（关节驱动绑定）
│
├── Description/                           命名空间 RobotSimulation.Robot.Description  [M2]
│   │  纯数据契约，收敛为 2 个文件：类型静态关联、随 URDF 规范同步演进；
│   │  文件内成员仍是独立 class/record；某类型长大后再抽独立文件（见 §4）
│   ├── RobotDescription.cs                # 描述模型聚合（纯数据、可视化子集）
│   │      RobotDescription            聚合根：Name?/Links/Joints/FindLink/RootLinks
│   │      Link                       Name；VisualElements
│   │                                  （无自原点：其坐标系由父关节 Origin 决定）
│   │      Joint                      Name/Type/ParentLinkName/ChildLinkName/
│   │                                  Origin(Matrix4x4, 行主序)/Axis(默认+X)
│   │      JointType                  Fixed/Revolute/Continuous/Prismatic
│   │      VisualElement              几何 + LocalTransform + 可选 Material
│   │      MaterialElement            Name?/Color(RGBA [0,1])?/TextureFile?
│   └── GeometryDescription.cs                # 几何族（record 多态，独立文件）
│          GeometryDescription            抽象 record
│          BoxGeometry(Vector3 Size)  SphereGeometry(float Radius)
│          CylinderGeometry(float Radius, float Length)   // 轴沿 +Z
│          CapsuleGeometry(float Radius, float Length)    // 轴沿 +Z
│          MeshGeometry(string Uri)                        // IAssetResolver 解析
│          独立原因：被 Core.Geometry / RobotModel 单独消费；未来 SDF 等格式复用
│
├── Urdf/                               命名空间 RobotSimulation.Robot.Urdf
│   ├── UrdfParser.cs                   internal sealed class UrdfParser           [M2]
│   │      RobotDescription Parse(XDocument root, string? baseDirectory)
│   │      三段式：①Load(根元素/禁用DTD) ②Read(link/joint/material) ③BuildTree(拓扑校验)
│   │
│   ├── UrdfParseException.cs           public sealed class UrdfParseException     [M2]
│   │      : Exception（消息含元素上下文，便于定位坏 URDF）
│   │
│   ├── UrdfElementReader.cs            internal static class UrdfElementReader    [M2]
│   │      容错属性读取：TryReadFloat/TryReadVector3/InvariantCulture；
│   │      缺失/非法 → 抛 UrdfParseException
│   │
│   └── AssetResolver.cs                public interface + 默认实现               [M2]
│          public interface IAssetResolver { string? Resolve(string uri, string baseDirectory); }
│          public sealed class FileSystemAssetResolver : IAssetResolver
│              // 绝对路径 / 相对 URDF 目录；package:// 返回 null（调用方告警跳过）
│
└── State/                              命名空间 RobotSimulation.Robot.State     [M3 ✅]
    └── RobotState.cs                    public sealed class RobotState（headless FK + 关节值）
        构造入参：RobotDescription；可驱动关节 = Revolute/Continuous/Prismatic（按描述顺序）
        SetJointValue / ApplyJointValues / GetLinkGlobalPose / RootPose
        纯计算、零渲染依赖；可视化驱动另由 Robot/RobotGameObject 完成
```

## 3. 可见性与公开面（对齐 api.md）

- **public（用户面）**：`RobotModel`、`RobotGameObject`、`IAssetResolver`、`FileSystemAssetResolver`、
  `UrdfParseException`、全部 `Description` 数据模型。
- **internal（实现细节）**：`UrdfParser`——用户只通过 `RobotModel.Parse/ParseFile` 进入；
  单测同样覆盖该入口。
- Robot 层不再出现任何渲染/GL 资源：`Instantiate()` 只产出 CPU 数据（MeshData/MaterialData）；
  GPU 实例化统一由 `Core/Rendering/Renderer` 在渲染线程完成。


## 4. 描述层文件聚合策略

`Description/` 只建 **2 个文件**，理由：

- 类型间强静态关联、随 URDF 规范同步演进、纯数据几乎无行为——多文件带来的定位收益低，
  收敛文件数能降低导航与维护开销；
- 文件内每个成员仍是**独立 class/record**（非嵌套类型），不损失可读性、可测试性与将来拆分能力；
- 拆分时机：某类型开始长出独立行为、或常被其它模块单独引用/修改时，再把它抽成独立文件即可。

`GeometryDescription.cs` 单独成文件是因为几何族被 `Core.Geometry` / `RobotModel` 单独
消费，且未来 SDF 等导入格式会复用同一族类型。

## 5. 关键解析行为约定（供 UrdfParser 实现遵守）

1. **数值解析一律 `CultureInfo.InvariantCulture`**（URDF 小数点恒为 `.`）；
2. **材质两遍收集**：先收集 robot 级与 link 内嵌的 `material name→定义`，再解析 visual 对
   name 的引用；同名定义后者覆盖（对齐多数 URDF 行为）；引用缺失时记录警告并置空（渲染用默认色）；
3. **rpy → 位姿矩阵**：URDF 为固定轴 XYZ（R = Rz(yaw)·Ry(pitch)·Rx(roll)），解析器合成
   `Matrix4x4` 存入 `Joint.Origin` / `VisualElement.LocalTransform`（行主序，与 Core.Scene.Transform 一致）；
   转换只实现一处（解析器内部辅助，避免散落）。
4. **拓扑校验**：link/joint 重名、joint 引用的 link 不存在、parent 回溯成环等错误上报；
   多根合法（"部分装配"）不报错——`RobotDescription.RootLinks` 由聚合根即时计算，描述层无回填字段；
5. **几何语义**：cylinder/capsule 轴沿 +Z；尺寸即 URDF size/radius/length，不做引擎侧缩放；
6. **Mesh 引用不在此层解析文件**：`MeshGeometry.Uri` 仅存原始字符串，由 Core/Geometry/Import（M2.5）
   在数据导入阶段经 `IAssetResolver` 消费，产物是 `MeshData`（随后由 Instantiate 挂到 visual 节点）。

## 6. 实现清单与验证状态（M2/M3 已完成）

1. ✅ `Robot/Description/`（两文件：`RobotDescription.cs` + `GeometryDescription.cs`）——纯数据；
2. ✅ `Robot/Urdf/`（`UrdfParser.cs` / `UrdfParseException.cs` / `AssetResolver.cs`）；
3. ✅ `Robot/RobotModel.cs`（单入口：静态解析 → `Description` → `Instantiate`）
   与 `Robot/RobotGameObject.cs`（`: GameObject`，附带关节驱动）；
4. ✅ `Robot/State/RobotState.cs`（headless FK / 关节值）；
5. ✅ `Core/Rendering/Renderer` + `MaterialData`（GameObject 数据化，GPU 收敛进渲染器）；
6. ✅ 验证：控制台样例（rpy / 多 visual / 全局材质 / cylinder / inertial 忽略 / 拓扑报错）+ demo 运行。

M4 的 Sink / 线条通道尚未实现。

## 7. 与 Core 的衔接

```
RobotModel.ParseFile("x.urdf")
   → Description（纯数据，headless 可用 / 可序列化 / 可测试 / 可多实例）
   → [M3] robot.Instantiate() → RobotGameObject（纯数据；scene.Add 后由 Renderer 绘制）
   → 关节驱动 robotGo.SetJointValue(...)；FK 用 RobotState
```
