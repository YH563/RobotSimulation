# Test point clouds (`Assets/PointClouds`)

Ready-to-use point-cloud samples for this host test. The minimal hosts load a single URDF and **no** point
cloud, so these files are here for when you want to exercise `PointCloudIo` / `PointCloud2Data`:
`PointCloud.FromFile(path)` and then `scene.Add(...)`. Both files are **generated, not downloaded**, so a
run never has to fetch anything: they are ~100 KB each, and their colour layout is the whole point of the
sample.

| File | Container | Colour channel | Shape |
| --- | --- | --- | --- |
| `rgb_cloud.ply` | Stanford PLY, `format ascii` | separate `red` / `green` / `blue` (uchar) | a cube whose six faces are six different colours |
| `rgb_cloud.pcd` | PCL PCD, `DATA ascii` | packed `rgb` (float32, bit pattern `0x00RRGGBB`) | a helix sweeping the colour wheel from bottom to top |

Regenerate both (from the repository root, requires only Python 3):

```bash
python3 docs/testing/tools/gencloud.py \
    src/BareWindowTest/Assets/PointClouds src/AvaloniaTest/Assets/PointClouds
```

## Why colour, and what actually verifies it

A cloud whose colour channels are ignored — or decoded with the wrong byte order, or packed as 0..1
instead of 0..255 — still renders fine; it just renders the material colour, or black. A screenshot
cannot tell those apart, so the check has to read the **decoded result** back: on the object returned by
`PointCloud.FromFile(...)`, `PointData` gives the point count and the first point's colour (e.g. 2904
points, `rgba(1.00, 0.16, 0.16)`). The two shapes are deliberate too: six flat faces and a smooth hue
sweep make a mis-decoded colour obvious to the eye.

## What the loader accepts

From `src/Core/Geometry/PointCloudIo.cs`:

| Container | Supported | Not supported (throws) |
| --- | --- | --- |
| `.pcd` (PCL) | `DATA ascii`, `DATA binary` | `DATA binary_compressed` |
| `.ply` (Stanford) | `format ascii`, `format binary_little_endian` | `format binary_big_endian`, list properties on `vertex` |

Per-point colour is recognised in the channel layouts `rgb`, `rgba`, `red`/`green`/`blue` (or `r`/`g`/`b`)
and `intensity`; without one of those, the material's single colour is used.

> Pitfall: most point clouds in PCL's own data repository (`PointCloudLibrary/data`, e.g.
> `tutorials/room_scan1.pcd`, `table_scene_mug_stereo_textured.pcd`) are `binary_compressed` and are
> therefore **not** readable here — do not download them expecting them to work.

## Optional downloads (not checked in)

Worth fetching only when a run needs real data; a host copies `Assets/`, so a large file stays local.

* Stanford 3D Scanning Repository (<https://graphics.stanford.edu/data/3Dscanrep/>) — `bun_zipper.ply`
  (35 947 points, ascii, carries `intensity`; the file's 69 451 `face` elements are skipped by the
  loader), plus dragon and Buddha.
* PCL data (<https://github.com/PointCloudLibrary/data>) — ascii samples that do load:
  `tutorials/lamppost.pcd` (1 771 points, smallest smoke case), `tutorials/ism_train_cat.pcd`,
  `tutorials/min_cut_segmentation_tutorial.pcd`, and the 100 000-point `biwi_face_database/model.pcd`
  (`DATA binary`).
  Take single files instead of cloning the whole repository, e.g.
  `git clone --depth 1 --filter=blob:none --sparse https://github.com/PointCloudLibrary/data` followed by
  `git sparse-checkout set --no-cone tutorials/lamppost.pcd`.
