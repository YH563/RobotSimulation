# 测试数据指南

`src/BareWindowTest` 与 `src/AvaloniaTest` 是本仓库的端到端检查，同时也是**最小的嵌入示例**：两者各自把**一个 URDF 机器人**装进一个默认场景、渲染出来，并打印一份两个宿主共用的日志。窗口里只有三件事——轨道相机（左键拖拽旋转 / 中键平移 / 右键或滚轮缩放）、点击选中高亮、以及库自带的地面网格与世界坐标轴。本文只讲这**一个文件**：它放在哪、从哪来、怎么换。

## 1. 数据放在哪里

```
src/BareWindowTest/Assets/        # 会被拷到 …/BareWindowTest/bin/<cfg>/net10.0/ 旁
src/AvaloniaTest/Assets/          # 会被拷到 …/AvaloniaTest/bin/<cfg>/net10.0/ 旁
├─ Models/
│  ├─ fairino3_v6/                # ★ 宿主加载的就是这一个：*.urdf + meshes/*.STL
│  ├─ primitives.urdf             # 备用样例：URDF 内置几何（见 §5）
│  ├─ urdf_tutorial/              # 备用样例：社区包（ROS），*.urdf + meshes/
│  ├─ formats/                    # 备用样例：每种 mesh 格式一个立方体
│  └─ README.md                   # 目录内说明：来源表、目录约定、可选下载
└─ PointClouds/
   ├─ rgb_cloud.ply               # 备用样例：彩色立方体，red/green/blue 通道
   ├─ rgb_cloud.pcd               # 备用样例：彩色螺旋，打包 rgb 通道
   └─ README.md
```

两个宿主测试**各存自己的一份**，而不是共享一个目录：这样任一工程都不必访问自己的目录之外，两者也可以各自改动。工程文件把 `Assets\**\*` 拷到可执行文件旁（`PreserveNewest`），宿主再以 `AppContext.BaseDirectory` 解析路径——因此**启动宿主时的工作目录不会影响加载内容**。

这里**没有路径参数，也没有路径表**：源码里的那一个常量（两个宿主中的 `ModelRelativePath`，分别位于 `src/BareWindowTest/Program.cs` 与 `src/AvaloniaTest/Controls/RobotViewportControl.cs`）**就是**约定本身；`--smoke [frames]` 是两个宿主唯一接受的开关。

## 2. 一次运行加载什么

| 条目（相对 `Assets/`） | 来源 | 覆盖点 |
| --- | --- | --- |
| `Models/fairino3_v6/fairino3_v6.urdf` | 本仓库自带 | 真机 6 轴机械臂：完整关节树 + 7 个 STL mesh。它引用的 `package://rus_sim_driver/meshes/…` 同时验证了包名前缀剥离与资源解析（包名与目录名刻意不一致） |

**一个 URDF 就够把整条链路走通**——XML 解析、`package://` 资源解析、STL 导入、link/joint 树、材质与光照——所以宿主只装它一个，代码因此小到可以当范例读。

相机也**不被覆盖**：`SceneGraph` 的构造函数已经摆好了网格地板、灯光、世界坐标轴和一个可用的相机位姿，宿主直接沿用。这本就是本库对嵌入方的承诺——`new SceneGraph()` 之后就是一个能看的场景，不需要任何调参。

`Assets/` 里其余样例数据仍然保留（见 §5），只是**最小示例不加载它们**。

## 3. 怎么运行

```bash
# 任一宿主：渲染 120 帧、打印 GPU + FPS、退出码 0（失败为 1）
dotnet run --project src/BareWindowTest -- --smoke 120
dotnet run --project src/AvaloniaTest  -- --smoke 120

# 不带任何参数：打开窗口一直运行到关窗（裸窗口宿主按 Esc 关闭）
dotnet run --project src/AvaloniaTest
```

两个宿主打印**相同的行**，这正是它们能互为交叉校验的原因：

```
Smoke mode: rendering 120 frame(s), then exiting.
GPU: Quadro P520/PCIe/SSE2 | vendor: NVIDIA Corporation | GL: 3.3.0 … | GLSL: 3.30 …
Test data: …/bin/Debug/net10.0/Assets
Loading model: …/Assets/Models/fairino3_v6/fairino3_v6.urdf
FPS 43.0 | last 19.21 ms | avg 23.24 ms | frames 29
Smoke OK: rendered 120 frame(s) with no error.
```

两份日志**唯一**允许的差异是 GL / GLSL 版本（Silk.NET 报 3.3 core，Avalonia 报 4.0）。文件缺失只会产生一行 `warn:` 并跳过该条目——运行永远不会因为测试数据失败，所以「只加了一半」的模型是可见的，而不是致命的。

冒烟模式证明的是：GL 上下文能建起来、着色器能编译、mesh 能上传、连续 N 帧无错误。它**不**断言画面内容（那需要截图比对），所以最小宿主里没有动画——帧计数来自 `IRenderer.Stats`，不依赖场景里有什么在动。

点击选中会打印命中的 link 名，这是交互链路（屏幕像素 → 世界射线 → 三角形求交 → 高亮）可用的直接证据：

```
Pick: selected 'shoulder_link:visual0'
Pick: nothing selected
```

## 4. 换一个模型

1. **把文件放到两个宿主相应 `Assets/Models/` 下**（也可以先只放进你正在试的那一个）。URDF 包用解析器认识的两种布局之一（细则见 `docs/robot/zh-CN.md` §3）：
   * **① 扁平式**（本仓库样例全是这种，**宿主无需额外参数**）——`.urdf` 直接放在包目录里、mesh 放在与它同级的 `meshes/` 下，以 `package://<任意包名>/meshes/<文件>` 引用；
   * **② 标准 ROS 式**——`.urdf` 在包根的 `urdf/` 下、mesh 在包根 `meshes/` 下，此时加载要把**包根**传给 `RobotModel.ParseFile` 的 `assetDirectory` 参数（宿主里那个调用需相应传参）。
   包名只作标识、**不会**去匹配目录名——本仓库的 `fairino3_v6` 里写的其实是 `package://rus_sim_driver/…`。另外**不要**提交 `*.urdf.xacro`（本引擎没有 xacro 展开器）。
2. **改 `ModelRelativePath`**：`src/BareWindowTest/Program.cs` 与 `src/AvaloniaTest/Controls/RobotViewportControl.cs` 顶部各一份，保证两个宿主加载同一个文件。
3. **跑一次宿主**（`--smoke 120`）并读日志：会打印它解析出的绝对路径，缺失的文件会被明确报告而不是静默忽略。

## 5. 备用样例数据（最小宿主不加载）

`Assets/` 里还留着一批**现成可用的样例**，它们覆盖了最小示例之外的代码路径。想验证某一条时，把对应路径填进 §4 的 `ModelRelativePath` 即可：

| 条目（相对 `Assets/`） | 来源 | 覆盖点 |
| --- | --- | --- |
| `Models/primitives.urdf` | 为本仓库手写 | URDF 内置几何：`box`、`sphere`、`cylinder`、`capsule`，各带 `<origin xyz rpy>`、机器人级 `<material>`、`<collision>` / `<inertial>` 与固定关节链 |
| `Models/urdf_tutorial/02-multipleshapes.urdf` | <https://github.com/ros/urdf_tutorial>（BSD-3-Clause） | 社区包：多个由内置几何拼出的 link，外加一个 DAE mesh |
| `Models/urdf_tutorial/05-visual.urdf` | 同一社区包 | DAE mesh + PNG 贴图（灰度版与彩色版） |
| `Models/formats/formats.urdf` | 为本仓库生成 | 同一个立方体分别导出为 STL、OBJ+MTL、COLLADA（DAE）、glTF 2.0、PLY（mesh）——一棵树覆盖五种格式 |
| `PointClouds/rgb_cloud.ply` | 生成（见下） | PLY ascii，独立的 `red`/`green`/`blue`（uchar）通道 |
| `PointClouds/rgb_cloud.pcd` | 生成（见下） | PCD ascii，打包的 `rgb`（float32）通道 |

点云要在场景里显示需要一个点云对象：`PointCloud.FromFile(path)`，然后 `scene.Add(...)`（最小宿主里已不再这么做，`PointCloud2Data` / `PointCloudIo` 仍在 `Core` 里，也仍在单元级可用）。

`formats/`（五种 mesh 格式的立方体）与 `PointClouds/` 下的两个文件都由为本仓库编写的脚本产生——不需要下载，因此 CI 从不依赖第三方可达。

```bash
# RGB 点云：彩色立方体（PLY，red/green/blue）与彩色螺旋（PCD，打包 rgb）
python3 docs/testing/tools/gencloud.py \
    src/BareWindowTest/Assets/PointClouds src/AvaloniaTest/Assets/PointClouds
```

点云样本要验证的是**颜色链路**而不只是文件链路：忽略颜色通道、字节序解错、或按 0..1 而不是 0..255 打包的点云**同样能渲染**，只是渲染成材质单色或全黑。所以判定要看**解码结果**（`PointCloud.FromFile(...)` 返回对象上的 `PointData`：点数与首个点的颜色），而不是截图；两个形状（六个平面 / 连续色相）也让解错颜色一眼可见。

可接受的容器格式（来自 `src/Core/Geometry/PointCloud/PointCloudIo.cs`）：

| 容器 | 支持 | 不支持（直接抛异常） |
| --- | --- | --- |
| `.pcd`（PCL） | `DATA ascii`、`DATA binary` | `DATA binary_compressed` |
| `.ply`（Stanford） | `format ascii`、`format binary_little_endian` | `format binary_big_endian`、`vertex` 上的 list 属性 |

逐点颜色识别 `rgb`、`rgba`、`red`/`green`/`blue`（或 `r`/`g`/`b`）与 `intensity`；都没有时按材质单色绘制。

## 6. 可选下载（永远不是必需）

只有确实需要真实数据时才去取——两个宿主都会把 `Assets/` 拷进输出，所以大文件留在本地、且不进版本库。

* **模型**：`go1_description`，来自 <https://github.com/unitreerobotics/unitree_ros>（`robots/go1_description/`，已是 xacro 展开结果，约 50 个 link、7 个 DAE mesh）；`ur_description` 或 `turtlebot3_description` 可再加带贴图的 DAE 机器人。避免 `*.urdf.xacro` 与 MoveIt 的 `panda_description`（只有 mesh，没有普通 `.urdf`）。
* **点云**：Stanford 3D Scanning Repository（<https://graphics.stanford.edu/data/3Dscanrep/>）的 `bun_zipper.ply`（35 947 点，ascii，`intensity` 着色）；以及 PCL 数据仓库（<https://github.com/PointCloudLibrary/data>）里可读的 ascii 样本，如 `tutorials/lamppost.pcd`（1 771 点，最小冒烟用例）、`tutorials/ism_train_cat.pcd`、`tutorials/min_cut_segmentation_tutorial.pcd` 与 10 万点的 `biwi_face_database/model.pcd`。该仓库里**大多数其他文件是 `binary_compressed`，本引擎读不了**。

## 7. 为什么两个宿主必须一致

本库的承诺是：各宿主之间**只有**「如何拿到 `GL` 与如何同步视口」不同。把两份日志格式与同一个被加载文件保持一致，就把这个承诺变成了脚本可校验的东西：跑两个宿主、对比输出，唯一可能不同的行只有 GL / GLSL 版本。

```bash
for f in /tmp/bare.log /tmp/avalonia.log; do
  grep -E '^      (Smoke|GPU|Test data|Loading|FPS)' "$f" \
    | sed -E 's#/src/(BareWindowTest|AvaloniaTest)/bin#/bin#; s/^      FPS .*/      FPS <measured>/' > "$f.cmp"
done
diff /tmp/bare.log.cmp /tmp/avalonia.log.cmp
```

FPS 行里的数字本就是每次测量的结果，所以脚本把它归一化掉；剩下的差异应当**只有** GL / GLSL 版本那一行。

---

## 8. 单元级检查（`src/RobotSimulation.Tests`）

两个宿主证明的是「渲染链路能跑起来」，证明不了「路径解析在边界上是对的」。这一层由 `src/RobotSimulation.Tests` 用 xunit 补上——它只引用 `Core` + `Robot`，不碰 GL：

| 类 | 覆盖 |
| --- | --- |
| `AssetResolverTests` | `FileSystemAssetResolver` 的根链顺序（`AssetDirectory` 先于 URDF 自身目录及其回退）、`package://` 包名前缀剥离、绝对路径直通、重复根去重，以及**未命中必须抛异常并列出所有尝试过的路径** |
| `RobotModelAssetResolutionTests` | 端到端 `RobotModel.ParseFile`：默认扁平布局（包名与目录名不一致）、五种 mesh 格式样例、tutorial 视觉样例；标准 ROS 布局在**不给** `assetDirectory` 时正确失败、给了就成功；`resolver` 与 `assetDirectory` 同时给出必须被拒绝 |

```bash
dotnet test src/RobotSimulation.Tests
```

数据**不**拷到输出目录：测试从仓库检出位置**原地**读取两棵资源树——宿主的 `src/BareWindowTest/Assets`（仓库的规范测试数据，Avalonia 宿主那份是逐字节副本），以及本工程自带的 `Assets/RosWorkspace/my_pkg/`（标准 ROS 包布局，仓库里唯一的这种样例，也正是 `assetDirectory` 存在的理由）。测试因此只在仓库检出内运行，`TestAssets.cs` 找不到 `RobotSimulation.sln` 会直接报错。

mesh 导入**不需要任何运行时初始化**：Assimp 的原生库随 `Silk.NET.Assimp` 包按 RID 分发，宿主什么都不必准备（早先 `AssimpNet` 时代那个 Linux `libdl.so` 兼容补丁已随依赖一起删除），因此本工程里没有任何模块初始化代码。


