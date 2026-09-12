# ROS-workspace fixture (`Assets/RosWorkspace`)

A hand-written fixture for the one URDF layout the two host tests do **not** carry: a standard ROS package.

```text
my_pkg/
├─ package.xml          # the ROS package marker (name + version; nothing in this repo reads it)
├─ urdf/robot.urdf      # one visual link, referencing package://my_pkg/meshes/cube.stl
└─ meshes/cube.stl      # a 0.4 m ASCII cube — readable, so the fixture needs no generator
```

This layout is the reason `assetDirectory` exists. The parser treats **the folder holding the `.urdf`** as the
package root of `package://<name>/…`, and here that folder is `my_pkg/urdf/` — which contains no meshes. The
load therefore fails, by design and with a message naming every path tried, until the caller passes the
**package root**:

```csharp
RobotModel.ParseFile("…/my_pkg/urdf/robot.urdf", assetDirectory: "…/my_pkg");
```

Both halves are asserted in `RobotModelAssetResolutionTests` (a missing `assetDirectory` must fail, a present
one must load), which makes this fixture the negative case behind the rule that a ROS package id is **never**
matched against a folder name.

Unlike the hosts' `Assets/` trees, nothing here is copied next to the test executable: the checks read this
folder **in place** from the repo checkout (see `TestAssets.cs` and `docs/testing` §8). It is therefore tracked
with the test project rather than duplicated into it.
