# Changelog

All notable changes to RobotSimulation are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

每个版本同时提供中文和 English 两部分；发布 workflow 会按 `## [x.y.z]` 章节
自动生成 GitHub Release 正文。

## [Unreleased]

## [0.5.1] - 2026-10-24

### 中文

#### 修复

- `RobotCollisionModel.DetectSelfCollision` 现在遵守 `GameObject.CollisionEnabled`：
  会沿着父链检查，禁用某个 link 或其祖先时，整棵子树退出自碰撞查询。
- 发布流程在推送前检查 nuget.org 是否已经存在该版本；已存在时同时跳过 nupkg 和
  snupkg，避免重跑时重建的 PDB 与已发布 DLL 的 MVID 不一致，导致符号包校验失败。

### English

#### Fixed

- `RobotCollisionModel.DetectSelfCollision` now respects `GameObject.CollisionEnabled`.
  It checks the ancestor chain, so disabling a link (or any ancestor) removes its
  whole subtree from self-collision queries.
- The publish workflow now checks whether the version already exists on nuget.org
  before pushing. Existing versions skip both nupkg and snupkg, preventing rebuilt
  PDBs from being compared against an already-published DLL with a different MVID.

## [0.5.0] - 2026-10-24

### 中文

#### 新增

- 碰撞检测（纯几何查询）：`BoxShape` / `SphereShape` / `CapsuleShape` /
  `CylinderShape` / `MeshShape`，AABB 宽相 + 图元/三角形 BVH 窄相，返回碰撞
  对、A→B 法线、穿透深度和近似接触点。
- `MeshShape` 按 `MeshData.Revision` 惰性构建 BVH；`CylinderShape` 在窄相中先按
  Capsule 近似。
- URDF `<collision>` 解析为 `CollisionElement`，并由 `RobotModel` 挂到 link
  节点；新增 `RobotModel.DetectSelfCollision`，默认跳过同一 link 和直接关节
  相邻的 link。
- `MeshSink` 新增 `enableCollision` / `SetCollisionEnabled`：外部重建网格可直接
  作为碰撞体，几何更新后按 revision 重建 BVH。
- Godot 风格半透明碰撞体显示：新增 `RenderPassKind.Debug` 与 `Debug.vert/frag`，
  低透明度蓝灰填充 + 柔和线框描边，不受深度遮挡；两个示例宿主都可用 `C` 键切换。
- 新增碰撞、URDF collision、机器人自碰撞和 Debug 显示测试，并同步中英文文档。

#### 说明

- 这是几何查询，不包含物理响应、关节动力学、CCD 或接触流形求解。
- 点云仍只作为可视化输入；重建环境需要外部后端先输出网格或体素碰撞代理。

### English

#### Added

- Collision queries (geometry only): `BoxShape` / `SphereShape` / `CapsuleShape` /
  `CylinderShape` / `MeshShape`, AABB broad phase plus primitive/triangle-BVH
  narrow phase, returning collision pairs, an A→B normal, penetration depth and
  an approximate contact point.
- `MeshShape` lazily builds its BVH from `MeshData.Revision`; `CylinderShape` is
  approximated as a capsule in the first narrow phase.
- URDF `<collision>` is parsed into `CollisionElement` and mounted on link nodes by
  `RobotModel`. New `RobotModel.DetectSelfCollision` skips shapes on the same link
  and directly joint-connected links by default.
- `MeshSink` gains `enableCollision` / `SetCollisionEnabled`: externally
  reconstructed chunks can participate in collision queries, with the BVH rebuilt
  from the mesh revision after geometry updates.
- Godot-style translucent collision fills: new `RenderPassKind.Debug` with
  `Debug.vert/frag`, a low-opacity blue-grey fill plus a soft wireframe outline,
  depth-independent; both host samples toggle it with `C`.
- Added collision, URDF-collision, robot self-collision and debug-display tests,
  with bilingual documentation updates.

#### Notes

- These are geometric queries only. Physics response, joint dynamics, CCD and
  contact-manifold solving are out of scope.
- Point clouds remain a visualization input; a reconstructed environment must
  first be turned into a mesh or voxel collision proxy by an external backend.

## [0.4.1] - 2026-10-06

### 中文

- Core/OpenGL：曲线和线通道支持像素线宽——新增 `GameObject.LineWidth` 与
  `Curve(..., lineWidth)`；Line pass 新增几何着色器 `Line.geom`，把线段扩成屏幕
  空间四边形，并按近平面裁剪跨相机线段，修掉换视角时 grid 掉线。
- OpenGL：`ShaderProgram` 支持可选的几何着色器阶段。
- 工程：`BareWindowTest` / `AvaloniaTest` 归入 `Tests/` 统一管理。
- 文档：同步中英文 README 与 docs。

### English

- Core/OpenGL: pixel line width for curves and the line pass — added
  `GameObject.LineWidth` and `Curve(..., lineWidth)`; the line pass gains the
  `Line.geom` geometry shader, expanding segments into screen-space quads and
  clipping eye-crossing segments against the near plane, fixing disappearing grid
  lines at some view angles.
- OpenGL: `ShaderProgram` accepts an optional geometry stage.
- Project: `BareWindowTest` / `AvaloniaTest` moved under `Tests/`.
- Docs: bilingual README and docs updated.

## [0.4.0] - 2026-10-02

### 中文

- 新增增量网格外部写入协议（Sink，整块替换）：
  - `MeshChunkUpdate` / `MeshChunkBatch`：ChunkId、Upsert/Remove、Origin、
    交错 float32 顶点、uint32 索引、可选 Bounds/Revision；传输无关的纯数据。
  - `MeshData` 增加 `Revision`、`SetGeometry(interleaved, indices)` 与 `Clear()`。
  - `ChunkedMesh` / `MeshSink`：任意线程 `Push`，属主线程 `Apply`，按 id 创建、
    替换、删除块；支持块级 revision 丢弃乱序旧更新。
  - OpenGL `Mesh.Sync(MeshData)` 按 revision 增量上传：未变零上传，变了用
    `BufferSubData`，超出容量才重建。
- 设计语义：每块整块替换、身份稳定（一个 `MeshData` 长期复用），让 marching
  cubes 类局部重三角化局限在块内，把每帧 GPU 上传控制在块级别。
- 测试与文档：新增 `MeshIncrementalTests`，同步 core/opengl 中英文文档。

### English

- Added the incremental-mesh external write protocol (Sink, whole-chunk
  replacement):
  - `MeshChunkUpdate` / `MeshChunkBatch`: ChunkId, Upsert/Remove, Origin,
    interleaved float32 vertices, uint32 indices, optional Bounds/Revision;
    transport-agnostic pure data.
  - `MeshData` gains `Revision`, `SetGeometry(interleaved, indices)` and `Clear()`.
  - `ChunkedMesh` / `MeshSink`: `Push` from any thread, `Apply` on the owner
    thread; creates, replaces and removes chunks by id, with an optional per-chunk
    revision that drops out-of-order updates.
  - OpenGL `Mesh.Sync(MeshData)` uploads by revision: no-op when unchanged,
    `BufferSubData` after a change, and a full rebuild only when capacity is
    exceeded.
- Semantics: whole-chunk replacement with stable identity (one long-lived
  `MeshData` per chunk), keeping marching-cubes-style local retriangulation inside
  a chunk and per-frame GPU uploads at chunk granularity.
- Tests and docs: added `MeshIncrementalTests`; core/opengl docs updated in both
  languages.

## [0.3.1] - 2026-09-21

### 中文

- 新增 `Axes.AlwaysOnTop`：单个 GameObject 的坐标系（`ShowLocalAxes`，也是
  `SceneGraph.Select` 的标记路径）画在几何之上；渲染器增加覆盖层，清一次深度
  缓冲后再画这些轴系。
- 默认仍为 false，`FixedWorldLength` 的尺子保持可遮挡；把
  `LocalAxes.AlwaysOnTop` 设回 false 可恢复普通遮挡。
- CI：符号包改为先推 nupkg（`--no-symbols`）、等待后再逐个推 snupkg，并增加
  3 次 30 秒重试，避开 nuget.org 的异步符号校验窗口。
- 新增 `AxesDepthPolicyTests`，同步中英文文档。

### English

- Added `Axes.AlwaysOnTop`: an individual GameObject's local axes
  (`ShowLocalAxes`, also the marker path used by `SceneGraph.Select`) are drawn
  above geometry; the renderer queues those sets, clears the depth buffer once,
  then draws them as an overlay.
- The default remains false, so `FixedWorldLength` rulers stay occludable; setting
  `LocalAxes.AlwaysOnTop` back to false restores ordinary depth behavior.
- CI: symbol packages are now pushed separately after the nupkgs
  (`--no-symbols`), with a wait and three 30-second retries to avoid nuget.org's
  asynchronous symbol-validation window.
- Added `AxesDepthPolicyTests`; bilingual docs updated.

## [0.3.0] - 2026-09-21

### 中文

- 线程契约：属主只声明一次（构造线程或开跑前 `ClaimOwnership()`）；帧边界不再
  把调用线程改成属主，非属主线程调用抛 `InvalidOperationException`。
- `Transform.Parent` 与 `Add` / `Remove` 统一入队：非属主线程在下一个帧边界生效，
  属主线程立即生效。队列增加 `PendingChangeCount` / `MaxPendingChanges` /
  `DroppedPendingChangeCount`。
- 破坏性 API：`Transform.Children` 改为 `IReadOnlyList<Transform>`；移除
  `SceneGraph.OrientationGizmoSize`，gizmo 尺寸改由渲染器按视口短边计算。
- 新增 `ClaimOwnership()` / `IsOwnerThread`，以及 `Light.WorldPosition` /
  `WorldDirection`。
- 修复重复 `Add` 后删除一次仍留在场景、灯光超出上限静默丢失、Avalonia 宿主
  未声明属主等问题。

### English

- Threading contract: ownership is declared once (the constructing thread or an
  explicit `ClaimOwnership()` before the loop); the frame boundary no longer
  silently makes the caller the owner, and off-owner calls throw
  `InvalidOperationException`.
- `Transform.Parent` and `Add` / `Remove` share one queue: off-owner calls commit
  at the next frame boundary, owner calls apply immediately. The queue now exposes
  `PendingChangeCount` / `MaxPendingChanges` / `DroppedPendingChangeCount`.
- Breaking API: `Transform.Children` is now `IReadOnlyList<Transform>`;
  `SceneGraph.OrientationGizmoSize` was removed and the gizmo size is derived from
  the viewport's shorter side.
- Added `ClaimOwnership()` / `IsOwnerThread`, plus `Light.WorldPosition` /
  `WorldDirection`.
- Fixed duplicate `Add` leaving a node in the scene after one `Remove`, silent
  light loss past the cap, and the Avalonia host not declaring its owner thread.

## [0.2.1] - 2026-09-20

### 中文

- 修复 URDF 关节链式合成顺序：`RobotModel` 的子 link 局部位姿改为
  `motion(q) * joint.Origin`，`RobotState` 改为
  `JointMotion(q) * joint.Origin * _poses[parent]`，与本库行向量约定下的
  `C_child = C_parent · C_origin · C_motion` 一致。
- 旧顺序会把关节 `origin` 的平移量甩出去，转动关节后出现最长约 1.3 m 的连杆
  错位；`RobotState` 连 `q = 0` 也不对（fairino3_v6 差 0.29 m）。
- 用独立列式 FK oracle 对拍 fairino3_v6、合成 4 轴链和带旋转的根节点，修复后
  36 项全通过，残差约 4e-7。

### English

- Fixed URDF joint-chain composition order: `RobotModel` now uses
  `motion(q) * joint.Origin` for child-link local poses, and `RobotState` uses
  `JointMotion(q) * joint.Origin * _poses[parent]`, matching the row-vector form
  of `C_child = C_parent · C_origin · C_motion`.
- The old order threw away the joint origin translation when rotating, producing
  link drift up to about 1.3 m; `RobotState` was wrong even at `q = 0` (0.29 m for
  fairino3_v6).
- Validated against an independent column-form FK oracle for fairino3_v6, a
  synthetic 4-axis chain and rotated root poses: 36/36 checks pass after the fix,
  with residuals around 4e-7.

## [0.2.0] - 2026-09-17

### 中文

- 新增拾取选中：`SceneGraph.Select` / `PickAndSelect` / `Selected`，选中即高亮
  并显示该对象自己的局部坐标轴（纯显示，无拖拽手柄）。
- 新增渲染器绘制的无背景屏幕空间朝向 gizmo；`Axes` 增加 `AxesSizing`
  （`ConstantScreenSize` / `FixedWorldLength`）、可写 `Length`、
  `AddDefaultWorldAxes()` 与 `FitWorldAxesToContent()`。
- 点云支持增量更新（追加 / 滑动窗口 + 局部 GPU 上传）。
- 世界坐标轴默认关闭；目标框架从 `net10.0` 调整为 `net8.0`。
- 修复 Avalonia 宿主在 Windows 上的 GL 上下文回退（WGL → ANGLE → Software），
  以及反复 `SetData` 时点云 GPU 缓冲泄漏。

### English

- Added pick-to-select: `SceneGraph.Select` / `PickAndSelect` / `Selected`,
  highlighting the picked object and showing its local axes (display only; no
  drag handles).
- Added the renderer's backdrop-free screen-space orientation gizmo; `Axes` gains
  `AxesSizing` (`ConstantScreenSize` / `FixedWorldLength`), writable `Length`,
  `AddDefaultWorldAxes()` and `FitWorldAxesToContent()`.
- Point clouds support incremental updates (append / sliding window + partial GPU
  upload).
- World axes are off by default; the target framework moved from `net10.0` to
  `net8.0`.
- Fixed the Avalonia host's GL-context fallback on Windows (WGL → ANGLE →
  Software) and a point-cloud GPU-buffer leak when `SetData` was called repeatedly.

## [0.1.0] - 2026-09-12

### 中文

- 首次发布 `RobotSimulation.Core` / `.Robot` / `.OpenGL` 三个 NuGet 包。
- Core：场景图、图元、纯 CPU 渲染数据、渲染抽象、模型导入（STL / OBJ / DAE /
  glTF）、点云（PCD / PLY）。
- Robot：URDF 描述、headless 正向运动学、机器人 GameObject 树。
- OpenGL：Silk.NET 后端、内置 Model / Line / Point / Skybox / Axes 着色器、
  材质与纹理、高亮与拾取。
- 可嵌入裸窗口 / Avalonia 宿主；目标框架 `net10.0`。

### English

- Initial release of the `RobotSimulation.Core` / `.Robot` / `.OpenGL` NuGet
  packages.
- Core: scene graph, primitives, pure-CPU render data, render abstraction, model
  import (STL / OBJ / DAE / glTF) and point clouds (PCD / PLY).
- Robot: URDF description, headless forward kinematics and the robot GameObject
  tree.
- OpenGL: Silk.NET backend, built-in Model / Line / Point / Skybox / Axes shaders,
  materials and textures, highlight and picking.
- Embeddable bare-window / Avalonia hosts; target framework `net10.0`.
