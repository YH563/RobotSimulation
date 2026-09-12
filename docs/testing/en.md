# Test data guide

`src/BareWindowTest` and `src/AvaloniaTest` are the repository's end-to-end check and, at the same time,
its **smallest embedding example**: each loads **one URDF robot** into a default scene, renders it, and
prints a log both hosts share. The window has exactly three behaviours — an orbit camera (left-drag
rotate, middle-drag pan, right-drag or the wheel zoom), pick-to-highlight, and the library's own grid
floor and world axes. This page is about that **one file**: where it lives, where it comes from, and how
to swap it.

## 1. Where the data lives

```
src/BareWindowTest/Assets/        # copied next to …/BareWindowTest/bin/<cfg>/net10.0/
src/AvaloniaTest/Assets/          # copied next to …/AvaloniaTest/bin/<cfg>/net10.0/
├─ Models/
│  ├─ fairino3_v6/                # ★ the one the hosts load: *.urdf + meshes/*.STL
│  ├─ primitives.urdf             # spare sample: URDF built-in geometry (see §5)
│  ├─ urdf_tutorial/              # spare sample: community package (ROS), *.urdf + meshes/
│  ├─ formats/                    # spare sample: one cube per mesh format
│  └─ README.md                   # per-folder notes: origin table, layout rule, downloads
└─ PointClouds/
   ├─ rgb_cloud.ply               # spare sample: colour cube, red/green/blue channels
   ├─ rgb_cloud.pcd               # spare sample: colour helix, packed rgb channel
   └─ README.md
```

Each host test keeps **its own** copy instead of sharing one folder, so neither project has to reach
outside its directory and the two can be changed independently. The project file copies `Assets\**\*`
next to the executable (`PreserveNewest`), and the host resolves its paths against
`AppContext.BaseDirectory` — therefore the working directory the host was started from cannot change what
is loaded.

There is **no path argument and no path table**: the single constant in the source (`ModelRelativePath` in
`src/BareWindowTest/Program.cs` and in `src/AvaloniaTest/Controls/RobotViewportControl.cs`) *is* the
contract. `--smoke [frames]` is the only switch either host accepts.

## 2. What one run loads

| Entry (relative to `Assets/`) | Origin | Covers |
| --- | --- | --- |
| `Models/fairino3_v6/fairino3_v6.urdf` | shipped with this repo | a real 6-axis arm: a full joint tree and 7 STL meshes. Its `package://rus_sim_driver/meshes/…` references also exercise package-prefix stripping and asset resolution (the package name deliberately does not match the folder name) |

**One URDF is enough to take the whole chain end to end** — XML parsing, `package://` asset resolution,
STL import, the link/joint tree, materials and lighting — so the host loads only that one and stays small
enough to read as an example.

The camera is left alone as well: `SceneGraph`'s constructor already assembles the grid floor, lights,
world axes and a usable camera pose, and the host just keeps them. That is precisely the promise this
library makes to an embedder — after `new SceneGraph()` you already have a scene worth looking at, with
nothing to tune.

The rest of the sample data stays in `Assets/` (see §5); it is simply **not loaded by the minimal hosts**.

## 3. Running

```bash
# Either host: render 120 frames, print GPU + FPS, exit with code 0 (or 1 on failure)
dotnet run --project src/BareWindowTest -- --smoke 120
dotnet run --project src/AvaloniaTest  -- --smoke 120

# No arguments: open the window and run until it is closed (Esc closes the bare-window host)
dotnet run --project src/AvaloniaTest
```

Both hosts print the same lines, which is what makes them a cross-check of each other:

```
Smoke mode: rendering 120 frame(s), then exiting.
GPU: Quadro P520/PCIe/SSE2 | vendor: NVIDIA Corporation | GL: 3.3.0 … | GLSL: 3.30 …
Test data: …/bin/Debug/net10.0/Assets
Loading model: …/Assets/Models/fairino3_v6/fairino3_v6.urdf
FPS 43.0 | last 19.21 ms | avg 23.24 ms | frames 29
Smoke OK: rendered 120 frame(s) with no error.
```

The **only** line allowed to differ between the two logs is the GL/GLSL version (Silk.NET reports 3.3 core,
Avalonia 4.0). A missing file only produces a `warn:` line and skips that entry — a run never fails because
of test data, so a half-added model is visible instead of fatal.

Smoke mode proves that the GL context comes up, the shaders compile, meshes upload and N consecutive frames
run without error. It does **not** assert what the picture looks like (that would need screenshot
comparison), which is why the minimal hosts animate nothing: the frame count comes from `IRenderer.Stats`
and does not depend on anything moving in the scene.

Clicking prints the picked link's name, which is direct evidence that the interaction path (screen pixel →
world ray → triangle intersection → highlight) works:

```
Pick: selected 'shoulder_link:visual0'
Pick: nothing selected
```

## 4. Swapping in another model

1. **Drop the file under both hosts' `Assets/Models/`** (or just the one you are experimenting with). A URDF
   package has to use one of the two layouts the parser understands (details in `docs/robot/en.md` §3):
   * **① flat** (every sample in this repo is like this, and it needs **no extra argument**): the `.urdf`
     sits directly in its package folder with its meshes in a sibling `meshes/`, referenced as
     `package://<any package name>/meshes/<file>`;
   * **② standard ROS**: the `.urdf` lives in `urdf/` under the package root and the meshes in `meshes/`
     under that root — loading then has to pass the **package root** as `RobotModel.ParseFile`'s
     `assetDirectory` argument (the call in the host needs that argument added).

   The package name is an identifier only and is **never** matched against a folder name — this repo's
   `fairino3_v6` actually references `package://rus_sim_driver/…`. Also, do not commit `*.urdf.xacro`
   (there is no xacro expander).
2. **Change `ModelRelativePath`** in both `src/BareWindowTest/Program.cs` and
   `src/AvaloniaTest/Controls/RobotViewportControl.cs`, so both hosts keep loading the same file.
3. **Run a host** (`--smoke 120`) and read the log: it prints the absolute path it resolved, and a missing
   file is reported instead of silently ignored.

## 5. Spare sample data (not loaded by the minimal hosts)

`Assets/` still ships a set of **ready-to-use samples** that cover the code paths beyond the minimal
example. To exercise one of them, put its path into the `ModelRelativePath` from §4:

| Entry (relative to `Assets/`) | Origin | Covers |
| --- | --- | --- |
| `Models/primitives.urdf` | written for this repo | URDF built-in geometry: `box`, `sphere`, `cylinder`, `capsule`, each with `<origin xyz rpy>`, a robot-level `<material>`, `<collision>` / `<inertial>` and a fixed-joint chain |
| `Models/urdf_tutorial/02-multipleshapes.urdf` | <https://github.com/ros/urdf_tutorial> (BSD-3-Clause) | a community package: several links built from built-in shapes, plus a DAE mesh |
| `Models/urdf_tutorial/05-visual.urdf` | same package | DAE meshes with PNG textures (the greyscale plus the colour variant) |
| `Models/formats/formats.urdf` | generated for this repo | the same cube exported as STL, OBJ+MTL, COLLADA (DAE), glTF 2.0 and PLY (mesh) — five formats in one tree |
| `PointClouds/rgb_cloud.ply` | generated (see below) | PLY ascii with separate `red`/`green`/`blue` (uchar) channels |
| `PointClouds/rgb_cloud.pcd` | generated (see below) | PCD ascii with a packed `rgb` (float32) channel |

Showing a cloud needs a point-cloud scene object: `PointCloud.FromFile(path)` followed by `scene.Add(...)`
(the minimal hosts no longer do that; `PointCloud2Data` / `PointCloudIo` still live in `Core` and still
work at the API level).

`formats/` (the five mesh-format cubes) and both `PointClouds/*` files are produced by scripts written for
this repository — nothing is downloaded, so CI never depends on a third party being reachable.

```bash
# RGB point clouds: a colour cube (PLY, red/green/blue) and a colour helix (PCD, packed rgb)
python3 docs/testing/tools/gencloud.py \
    src/BareWindowTest/Assets/PointClouds src/AvaloniaTest/Assets/PointClouds
```

The point-cloud samples exist to prove the colour path, not just the file path: a cloud whose channels are
ignored — or decoded with the wrong byte order, or packed as 0..1 instead of 0..255 — still renders, it just
renders the material colour or black. So the check has to look at the **decoded result** (the `PointData` of
the object returned by `PointCloud.FromFile(...)`: the point count and the first point's colour) rather than
at a screenshot; the two shapes (six flat faces / a smooth hue sweep) make a wrong decode obvious to the eye
as well.

Accepted containers, from `src/Core/Geometry/PointCloud/PointCloudIo.cs`:

| Container | Supported | Not supported (throws) |
| --- | --- | --- |
| `.pcd` (PCL) | `DATA ascii`, `DATA binary` | `DATA binary_compressed` |
| `.ply` (Stanford) | `format ascii`, `format binary_little_endian` | `format binary_big_endian`, list properties on `vertex` |

Per-point colour is recognised as `rgb`, `rgba`, `red`/`green`/`blue` (or `r`/`g`/`b`) or `intensity`;
without one of those the cloud is drawn in the material's single colour.

## 6. Optional downloads (never required)

Only fetch these when a run actually needs real data — both hosts copy `Assets/`, so a large file stays
local and out of version control.

* **Models**: `go1_description` from <https://github.com/unitreerobotics/unitree_ros>
  (`robots/go1_description/`, already xacro-expanded, ~50 links, 7 DAE meshes); `ur_description` or
  `turtlebot3_description` for more DAE/textured robots. Avoid `*.urdf.xacro` and MoveIt's
  `panda_description` (meshes only, no plain `.urdf`).
* **Point clouds**: Stanford 3D Scanning Repository (<https://graphics.stanford.edu/data/3Dscanrep/>) —
  `bun_zipper.ply`, 35 947 points, ascii, `intensity` colour; and ascii samples from PCL's data
  (<https://github.com/PointCloudLibrary/data>) such as `tutorials/lamppost.pcd` (1 771 points, the
  smallest smoke case), `tutorials/ism_train_cat.pcd`, `tutorials/min_cut_segmentation_tutorial.pcd` and
  the 100 000-point `biwi_face_database/model.pcd`. Most *other* files in that repository are
  `binary_compressed` and are not readable here.

## 7. Why the two hosts must agree

The library's promise is that only "how `GL` is obtained and how the viewport is synchronized" differs per
host. Keeping both log formats and the same loaded file identical turns that promise into something a
script can check: run both hosts, diff the output, and the only line that may differ is the GL/GLSL
version.

```bash
for f in /tmp/bare.log /tmp/avalonia.log; do
  grep -E '^      (Smoke|GPU|Test data|Loading|FPS)' "$f" \
    | sed -E 's#/src/(BareWindowTest|AvaloniaTest)/bin#/bin#; s/^      FPS .*/      FPS <measured>/' > "$f.cmp"
done
diff /tmp/bare.log.cmp /tmp/avalonia.log.cmp
```

The numbers on the FPS line are measurements, so the script normalises them away; what is left should differ in
**exactly one** line — the GL / GLSL version.

---

## 8. Unit-level checks (`src/RobotSimulation.Tests`)

The two hosts prove that the *render pipeline comes up*; they cannot prove that *path resolution is correct at
the edges*. `src/RobotSimulation.Tests` covers that with xunit. It references only `Core` + `Robot` and never
touches GL:

| Class | Covers |
| --- | --- |
| `AssetResolverTests` | the root chain of `FileSystemAssetResolver` (`AssetDirectory` before the URDF's own directory, and the fallback to it), `package://` prefix stripping, absolute paths passing through, identical roots collapsing, and **a miss throwing with every tried path listed** |
| `RobotModelAssetResolutionTests` | end-to-end `RobotModel.ParseFile`: the default flat layout (package name differing from the folder name), the five-format mesh sample, the tutorial visual sample; the standard ROS layout failing without `assetDirectory` and loading with it; `resolver` + `assetDirectory` together being rejected |

```bash
dotnet test src/RobotSimulation.Tests
```

Nothing is copied to the output directory: the checks read two asset trees **in place** from the repo
checkout — the hosts' `src/BareWindowTest/Assets` (the repo's canonical test data; the Avalonia host carries a
byte-identical copy) and this project's own `Assets/RosWorkspace/my_pkg/` (a standard ROS package layout, the
only sample of its kind here, and the reason `assetDirectory` exists). The checks therefore only run from a
repo checkout; `TestAssets.cs` fails loudly when it cannot find `RobotSimulation.sln`.

Mesh import needs no runtime initialization at all: Assimp's native library ships per-RID inside the
`Silk.NET.Assimp` package, so a host has nothing to prepare (the Linux `libdl.so` compatibility patch from
the `AssimpNet` era went away together with that dependency), and this project carries no module initializer.


