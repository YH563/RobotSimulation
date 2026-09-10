# Test data guide

`src/BareWindowTest` and `src/AvaloniaTest` are the repository's end-to-end check: each loads one fixed
data set into a scene, renders it, and prints a log both hosts share. This page is about that data set —
what is loaded, where every file comes from, and how to add, replace or regenerate it.

## 1. Where the data lives

```
src/BareWindowTest/Assets/        # copied next to …/BareWindowTest/bin/<cfg>/net10.0/
src/AvaloniaTest/Assets/          # copied next to …/AvaloniaTest/bin/<cfg>/net10.0/
├─ Models/
│  ├─ primitives.urdf             # written for this repo (URDF built-in geometry)
│  ├─ urdf_tutorial/              # community package (ROS), *.urdf + meshes/
│  ├─ fairino3_v6/                # shipped with this repo, *.urdf + meshes/*.STL
│  ├─ formats/                    # generated: one cube per mesh format
│  └─ README.md                   # per-folder notes: origin table, layout rule, downloads
└─ PointClouds/
   ├─ rgb_cloud.ply               # generated: colour cube, red/green/blue channels
   ├─ rgb_cloud.pcd               # generated: colour helix, packed rgb channel
   └─ README.md
```

Each host test keeps **its own** copy (≈6 MB each) instead of sharing one folder, so neither project has
to reach outside its directory and the two can be changed independently. The project file copies
`Assets\**\*` next to the executable (`PreserveNewest`), and the host resolves its paths against
`AppContext.BaseDirectory` — therefore the working directory the host was started from cannot change what
is loaded.

There is **no path argument and no shared path table**: the table in the source
(`ModelRelativePaths` / `PointCloudRelativePaths` in `Program.LoadTestData` / `RobotViewportControl
.LoadTestData`, identical in both hosts) *is* the contract. `--smoke [frames]` is the only switch either
host accepts.

## 2. What one run loads

| Entry (relative to `Assets/`) | Origin | Covers |
| --- | --- | --- |
| `Models/primitives.urdf` | written for this repo | URDF built-in geometry: `box`, `sphere`, `cylinder`, `capsule`, each with `<origin xyz rpy>`, a robot-level `<material>`, `<collision>` / `<inertial>` and a fixed-joint chain |
| `Models/urdf_tutorial/02-multipleshapes.urdf` | <https://github.com/ros/urdf_tutorial> (BSD-3-Clause) | a community package: several links built from built-in shapes, plus a DAE mesh |
| `Models/urdf_tutorial/05-visual.urdf` | same package | DAE meshes with PNG textures (the greyscale plus the colour variant) |
| `Models/fairino3_v6/fairino3_v6.urdf` | shipped with this repo | a real 6-axis arm: a full joint tree and 7 STL meshes |
| `Models/formats/formats.urdf` | generated for this repo | the same cube exported as STL, OBJ+MTL, COLLADA (DAE), glTF 2.0 and PLY (mesh) — five formats in one tree |
| `PointClouds/rgb_cloud.ply` | generated (see §5) | PLY ascii with separate `red`/`green`/`blue` (uchar) channels |
| `PointClouds/rgb_cloud.pcd` | generated (see §5) | PCD ascii with a packed `rgb` (float32) channel |

All of it goes into one scene — models spaced along X, point clouds floating above them, and a spinning
box in front of the camera — so a single run exercises every kind of test data.

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
Loading model: …/Assets/Models/primitives.urdf
…
Loading point cloud: …/Assets/PointClouds/rgb_cloud.ply (2904 points, color: first point rgba(1.00, 0.16, 0.16))
Loading point cloud: …/Assets/PointClouds/rgb_cloud.pcd (2000 points, color: first point rgba(1.00, 0.00, 0.00))
FPS 43.0 | last 19.21 ms | avg 23.24 ms | frames 29
Smoke OK: rendered 120 frame(s) with no error.
```

The **only** expected difference between the two logs is the GL/GLSL version (Silk.NET reports a 3.3 core
context, Avalonia a 4.0 one). A missing data file is a `warn:` line and the entry is skipped — a run never
fails because of test data, so a half-added model is visible rather than fatal.

## 4. Adding or replacing data

1. **Drop the file(s) under the right `Assets/` folder** of both hosts (or of the one you are
   experimenting with). For a URDF package keep the layout the URDF expects: the `.urdf` directly in its
   package folder, meshes in a `meshes/` folder next to it, referenced as
   `package://<any-name>/meshes/<file>` — the parser treats the URDF's own folder as the package root.
   Do **not** add an extra `urdf/` level, and do not commit `*.urdf.xacro` (there is no xacro expander).
2. **Add the relative path to the table** at the top of `src/BareWindowTest/Program.cs` and of
   `src/AvaloniaTest/Controls/RobotViewportControl.cs`, so both hosts keep loading the same set in the
   same order.
3. **Run a host** (`--smoke 120`) and read the log: each entry prints the absolute path it resolved, and a
   missing file is reported instead of silently ignored.

## 5. Regenerating the generated samples

`formats/` (the five mesh-format cubes) and both `PointClouds/*` files are produced by scripts written for
this repository — nothing is downloaded, so CI never depends on a third party being reachable.

```bash
# RGB point clouds: a colour cube (PLY, red/green/blue) and a colour helix (PCD, packed rgb)
python3 docs/testing/tools/gencloud.py \
    src/BareWindowTest/Assets/PointClouds src/AvaloniaTest/Assets/PointClouds
```

The point-cloud samples exist to prove the colour path, not just the file path: a cloud whose channels are
ignored — or decoded with the wrong byte order, or packed as 0..1 instead of 0..255 — still renders, it just
renders the material colour or black. That is why each host logs the point count **and the decoded first
point colour**, and why the two shapes (six flat faces / a smooth hue sweep) make a wrong decode obvious to
the eye as well.

Accepted containers, from `src/Core/Geometry/PointCloudIo.cs`:

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
host. Keeping both data tables and both log formats identical turns that promise into something a script
can check: run both hosts, diff the output, and the only line that may differ is the GL/GLSL version.

```bash
for f in /tmp/bare.log /tmp/avalonia.log; do
  grep -E '^      (Smoke|GPU|Test data|Loading|FPS)' "$f" \
    | sed -E 's#/src/(BareWindowTest|AvaloniaTest)/bin#/bin#' > "$f.cmp"
done
diff /tmp/bare.log.cmp /tmp/avalonia.log.cmp
```
