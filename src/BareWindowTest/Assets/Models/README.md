# Test models (`Assets/Models`)

The URDF models available to this host test. The host loads **one** of them: the name in `ModelRelativePath` at
the top of `src/BareWindowTest/Program.cs`, and in `Controls/RobotViewportControl.cs` of the Avalonia host. The
rest are ready-to-use samples — change that one constant to switch, and see `docs/testing` for the layouts and
provenance. The project file copies this folder next to the executable, so those paths resolve from the
executable directory — whatever working directory the host was started from.

| Path | Origin | What it covers |
| --- | --- | --- |
| `primitives.urdf` | written for this repo, no download | URDF built-in geometry: `box`, `sphere`, `cylinder`, `capsule` — each with `<origin xyz rpy>`, a robot-level `<material>`, plus `<collision>` / `<inertial>` blocks and a fixed-joint chain |
| `urdf_tutorial/` | <https://github.com/ros/urdf_tutorial> (BSD-3-Clause) | a community package: `02-multipleshapes.urdf` (built-in shapes + one DAE mesh) and `05-visual.urdf` (DAE meshes + PNG textures) |
| `fairino3_v6/` | shipped with this repository (a 6-axis arm description) | a real robot: a full joint tree and STL meshes |
| `formats/` | generated for this repo | one 0.4 m cube per mesh format the importer reads: STL, OBJ+MTL, COLLADA (DAE), glTF 2.0, PLY |

## Layout rule (the one thing that breaks silently)

The parser treats **the folder containing the `.urdf`** as the package root of `package://<name>/…`.
A URDF therefore has to sit directly in its package folder (`fairino3_v6/fairino3_v6.urdf`) with its
meshes in a `meshes/` folder next to it. Do not add an extra `urdf/` level: `package://urdf_tutorial/
meshes/l_finger.dae` would then no longer resolve, and the meshes would silently not appear.

## Adding data

1. Drop the package under this folder (keep the layout above).
2. Point `ModelRelativePath` (the single constant) at the `.urdf` path, relative to `Assets/`, in **both**
   hosts so they keep loading the same file.
3. Run a host: a missing file is logged and skipped instead of failing the run, so a half-added model is
   easy to spot in the output.

## Optional downloads (not checked in)

Fetch these only when a run needs them — a host copies `Assets/`, so a large download stays local.

* `go1_description` from <https://github.com/unitreerobotics/unitree_ros> (`robots/go1_description/`):
  a quadruped with ~50 links and 7 DAE meshes. Its `.urdf` is already xacro-expanded, so it parses as is.
* `ur_description` (ROS) or `turtlebot3_description`: more DAE/textured robots with a real joint tree.
* **Avoid** files that are really xacro — `*.urdf.xacro`, `turtlebot3_burger.urdf` (an `.urdf` name on
  xacro content) or MoveIt's `panda_description` (meshes only, the URDF exists only as `.xacro`): this
  engine has no xacro expander.
