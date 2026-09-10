# 测试数据指南

`src/BareWindowTest` 与 `src/AvaloniaTest` 是本仓库的端到端检查：两者各自把一份固定数据集装进场景、渲染出来，并打印一份两个宿主共用的日志。本文只讲这份数据：加载了什么、每个文件从哪里来、如何新增 / 替换 / 重新生成。

## 1. 数据放在哪里

```
src/BareWindowTest/Assets/        # 会被拷到 …/BareWindowTest/bin/<cfg>/net10.0/ 旁
src/AvaloniaTest/Assets/          # 会被拷到 …/AvaloniaTest/bin/<cfg>/net10.0/ 旁
├─ Models/
│  ├─ primitives.urdf             # 为本仓库手写（URDF 内置几何）
│  ├─ urdf_tutorial/              # 社区包（ROS），*.urdf + meshes/
│  ├─ fairino3_v6/                # 仓库自带，*.urdf + meshes/*.STL
│  ├─ formats/                    # 生成：每种 mesh 格式一个立方体
│  └─ README.md                   # 目录内说明：来源表、目录约定、可选下载
└─ PointClouds/
   ├─ rgb_cloud.ply               # 生成：彩色立方体，red/green/blue 通道
   ├─ rgb_cloud.pcd               # 生成：彩色螺旋，打包 rgb 通道
   └─ README.md
```

两个宿主测试**各存自己的一份**（每份约 6 MB），而不是共享一个目录：这样任一工程都不必访问自己的目录之外，两者也可以各自改动。工程文件把 `Assets\**\*` 拷到可执行文件旁（`PreserveNewest`），宿主再以 `AppContext.BaseDirectory` 解析路径——因此**启动宿主时的工作目录不会影响加载内容**。

这里**没有路径参数，也没有共享路径表**：源码里的表格（两个宿主中的 `ModelRelativePaths` / `PointCloudRelativePaths`，位于 `Program.LoadTestData` / `RobotViewportControl.LoadTestData`）**就是**约定本身；`--smoke [frames]` 是两个宿主唯一接受的开关。

## 2. 一次运行加载什么

| 条目（相对 `Assets/`） | 来源 | 覆盖点 |
| --- | --- | --- |
| `Models/primitives.urdf` | 为本仓库手写 | URDF 内置几何：`box`、`sphere`、`cylinder`、`capsule`，各带 `<origin xyz rpy>`、机器人级 `<material>`、`<collision>` / `<inertial>` 与固定关节链 |
| `Models/urdf_tutorial/02-multipleshapes.urdf` | <https://github.com/ros/urdf_tutorial>（BSD-3-Clause） | 社区包：多个由内置几何拼出的 link，外加一个 DAE mesh |
| `Models/urdf_tutorial/05-visual.urdf` | 同一社区包 | DAE mesh + PNG 贴图（灰度版与彩色版） |
| `Models/fairino3_v6/fairino3_v6.urdf` | 本仓库自带 | 真机 6 轴机械臂：完整关节树 + 7 个 STL mesh |
| `Models/formats/formats.urdf` | 为本仓库生成 | 同一个立方体分别导出为 STL、OBJ+MTL、COLLADA（DAE）、glTF 2.0、PLY（mesh）——一棵树覆盖五种格式 |
| `PointClouds/rgb_cloud.ply` | 生成（见 §5） | PLY ascii，独立的 `red`/`green`/`blue`（uchar）通道 |
| `PointClouds/rgb_cloud.pcd` | 生成（见 §5） | PCD ascii，打包的 `rgb`（float32）通道 |

全部装进**同一个场景**——模型沿 X 排开、点云悬在上方、相机前再放一个自转的立方体——因此一次运行即可覆盖所有类型的测试数据。

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
Loading model: …/Assets/Models/primitives.urdf
…
Loading point cloud: …/Assets/PointClouds/rgb_cloud.ply (2904 points, color: first point rgba(1.00, 0.16, 0.16))
Loading point cloud: …/Assets/PointClouds/rgb_cloud.pcd (2000 points, color: first point rgba(1.00, 0.00, 0.00))
FPS 43.0 | last 19.21 ms | avg 23.24 ms | frames 29
Smoke OK: rendered 120 frame(s) with no error.
```

两份日志**唯一**允许的差异是 GL / GLSL 版本（Silk.NET 报 3.3 core，Avalonia 报 4.0）。文件缺失只会产生一行 `warn:` 并跳过该条目——运行永远不会因为测试数据失败，所以"只加了一半"的模型是可见的，而不是致命的。

## 4. 新增 / 替换数据

1. **把文件放到两个宿主相应 `Assets/` 目录下**（也可以先只放进你正在试的那一个）。URDF 包要遵守解析器期待的目录约定：`.urdf` 直接放在包目录里、mesh 放在与它同级的 `meshes/` 下，并以 `package://<任意包名>/meshes/<文件>` 引用——解析器把 **URDF 自己的目录**当作包根。**不要**再多套一层 `urdf/`，也不要提交 `*.urdf.xacro`（本引擎没有 xacro 展开器）。
2. **把相对路径加进表格**：`src/BareWindowTest/Program.cs` 与 `src/AvaloniaTest/Controls/RobotViewportControl.cs` 顶部各一份，保证两个宿主按同样顺序加载同一批数据。
3. **跑一次宿主**（`--smoke 120`）并读日志：每个条目都会打印它解析出的绝对路径，缺失的文件会被明确报告而不是静默忽略。

## 5. 重新生成生成的样本

`formats/`（五种 mesh 格式的立方体）与 `PointClouds/` 下的两个文件都由为本仓库编写的脚本产生——不需要下载，因此 CI 从不依赖第三方可达。

```bash
# RGB 点云：彩色立方体（PLY，red/green/blue）与彩色螺旋（PCD，打包 rgb）
python3 docs/testing/tools/gencloud.py \
    src/BareWindowTest/Assets/PointClouds src/AvaloniaTest/Assets/PointClouds
```

点云样本要验证的是**颜色链路**而不只是文件链路：忽略颜色通道、字节序解错、或按 0..1 而不是 0..255 打包的点云**同样能渲染**，只是渲染成材质单色或全黑。所以宿主会打印点数**和首个点的解码颜色**，两个形状（六个平面 / 连续色相）也让解错颜色一眼可见。

可接受的容器格式（来自 `src/Core/Geometry/PointCloudIo.cs`）：

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

本库的承诺是：各宿主之间**只有**「如何拿到 `GL` 与如何同步视口」不同。把两份数据表与两份日志格式保持一致，就把这个承诺变成了脚本可校验的东西：跑两个宿主、对比输出，唯一可能不同的行只有 GL / GLSL 版本。

```bash
for f in /tmp/bare.log /tmp/avalonia.log; do
  grep -E '^      (Smoke|GPU|Test data|Loading|FPS)' "$f" \
    | sed -E 's#/src/(BareWindowTest|AvaloniaTest)/bin#/bin#' > "$f.cmp"
done
diff /tmp/bare.log.cmp /tmp/avalonia.log.cmp
```
