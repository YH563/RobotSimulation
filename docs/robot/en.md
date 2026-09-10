# RobotSimulation.Robot Public API (English)

> Status: reflects the current code. Namespace: `RobotSimulation.Robot.*` · Depends on: `RobotSimulation.Core`.
> Companion: `../architecture/en.md`, `../core/en.md`, `../opengl/en.md`.

`Robot` is the robot domain model: it parses URDF / any description into a pure-data description and offers two consumption paths — ① `RobotModel` (a `GameObject` tree for visualization) and ② `RobotState` (headless joint state + forward kinematics). `Core` never references `Robot`; `Robot` references `Core` only for CPU data (`MeshData` / `MaterialData` / `Scene`) and `Logger`.

---

## 1. `RobotModel` (robot = a GameObject tree)

`RobotModel : GameObject`. It is the root of the robot tree (the root link's skeleton node is itself) and the single entry point to URDF/descriptions: read a URDF or build from any `RobotDescription` to get a drivable, extensible robot `GameObject` tree for `scene.Add`. Nodes hold only CPU data.

### Construction & factories

```csharp
// URDF text → full robot tree
public static RobotModel Parse(string urdfXml, string? baseDirectory = null, IAssetResolver? resolver = null);

// Read a URDF file → full robot tree (the file directory becomes the base for relative asset paths)
public static RobotModel ParseFile(string path, IAssetResolver? resolver = null);

// Extension point: any description source (SDF/custom) → the same robot tree
public RobotModel(RobotDescription description, IAssetResolver? resolver = null);
```

Requires `description` to have exactly one root link; multiple roots are not an error ("partial assembly") but building a single `GameObject` needs a single root.

### Properties

| Member | Type | Notes |
|---|---|---|
| `Description` | `RobotDescription` | The pure-data description this tree was built from (headless / serialization / multi-instance reuse) |
| `RobotName` | `string` | URDF `<robot name>`; distinct from `GameObject.Name` (root link name) |
| `AssetResolver` | `IAssetResolver?` | The asset path resolver in use |
| `DrivableJoints` | `IReadOnlyList<Joint>` | Drivable joints (Revolute/Continuous/Prismatic, in description order) |
| `DrivableJointCount` | `int` | Number of drivable joints (array length required by `ApplyJointValues`) |
| `RootPose` | `Matrix4x4` | Robot overall (root link) pose in its parent frame; written via built-in interfaces |

### Methods

```csharp
public RobotState CreateState();                                  // headless joint state + FK
public void SetJointValue(string name, float value);              // drive by name (radians rotation / meters translation), immediately updating the child link's local Transform
public void ApplyJointValues(IReadOnlyList<float> values);        // batch drive in drivable order
```

> Joints only act on `Transform` (pure data), never touching OpenGL. After construction the whole subtree is `Transform` read-only locked; child link poses can only be written via `RobotModel` built-in interfaces or `RootPose`, not by external direct mutation.

### Usage

```csharp
using RobotSimulation.Robot;

var robot = RobotModel.ParseFile("arm.urdf");
scene.Add(robot);                       // added equally with other GameObjects

robot.SetJointValue("shoulder", 1.2f);  // revolute: radians
robot.SetJointValue("slider", 0.35f);   // prismatic: meters
robot.RootPose = Matrix4x4.CreateTranslation(new Vector3(1, 0, 0));

// headless FK state object (not attached to a scene; multiple instances allowed)
RobotState state = robot.CreateState();
state.SetJointValue("shoulder", 1.2f);
Matrix4x4 ee = state.GetLinkGlobalPose("tool0");
```

---

## 2. Description data model (`Description/`)

> Namespace `RobotSimulation.Robot.Description`. Pure data, zero render deps, serializable; covers only visualization-relevant info, ignoring physical parameters (inertial, collision, etc).

- `RobotDescription`: `Name` (`<robot name>`), `RootLink`, `Links` / `Joints` (`IReadOnlyList`).
- `Link`: `Name`, `Visual` (root visual) or `Visuals` (multiple), optional `Inertial` (unused).
- `VisualElement`: `Name`, `Geometry`, `MaterialName`, `LocalTransform` (row-major `Matrix4x4`), optional `Visible`.
- `Joint`: `Name`, `Type`, `ParentLink`, `ChildLink`, `Origin` (`Matrix4x4`), `Axis`, `Limits`, `Mimic`.
  - `JointType`: `Revolute/Continuous/Prismatic/Fixed/Planar/Floating`.
  - A joint is "given a joint value → child link's transform relative to the parent link". Revolute/Continuous rotate around `Axis`; Prismatic translates along `Axis`; Fixed has no DOF.
- `Geometry` base:
  - `BoxGeometry(Vector3 Size)`
  - `SphereGeometry(float Radius)`
  - `CylinderGeometry(float Radius, float Length)` — axis along +Z
  - `CapsuleGeometry(float Radius, float Length)` — axis along +Z; total height = Length + 2×Radius
  - `MeshGeometry(string Uri)` — stores only the raw reference string (relative path / `package://`), not resolving files at this layer

---

## 3. URDF (`Urdf/`)

- `UrdfParser` (`internal`): `RobotDescription Parse(XDocument document, string? baseDirectory)`. Handles only visualization: robot/link/visual/geometry/material/joint (topology); explicitly ignores inertial/collision/transmission/gazebo; poses unified as row-major matrices; numbers parsed with `InvariantCulture`. Users enter via `RobotModel.Parse/ParseFile`.
- `UrdfParseException` (`public`): thrown on parse failure; the message includes element/attribute context to help locate the file.

### `IAssetResolver` / `FileSystemAssetResolver`
Asset-location extension point: resolve a URDF mesh/texture reference string into an accessible absolute path.

```csharp
public interface IAssetResolver { string? Resolve(string uri, string? baseDirectory); }
```

Default `FileSystemAssetResolver` rules:
1. Absolute paths resolved as absolute;
2. Relative paths merged relative to the URDF file directory (`baseDirectory`);
3. `package://package-name/rest` strips the prefix and looks up "the URDF's sibling directory as package root";
4. After resolution, existence is still validated; missing throws `FileNotFoundException` (logged via `Logger`), never silently skipped.

### `UrdfLocator`
Locates `.urdf` files (distinct from "URDF internal asset resolution"); the host composition root decides whether to load a robot.

```csharp
public static string? UrdfLocator.Find(string? argument, params string?[]? extraSearchRoots);
```

Looks under `AppContext.BaseDirectory`, the current directory, and additional search roots, recursively under each root's `Assets/Models` (the folder each host test copies next to its own executable); if `argument` is an existing file path it is returned directly (absolutized).

---

## 4. State / headless forward kinematics (`State/`)

`RobotState` maintains drivable joint values over `RobotDescription` and computes forward kinematics (FK). Pure computation, no render/GL/UI dependency; usable headless (trajectory, physics, and visualization can share one state source).

| Member | Type | Notes |
|---|---|---|
| `Description` | `RobotDescription` | Static description used at construction |
| `DrivableJoints` | `IReadOnlyList<Joint>` | Drivable joints (Revolute/Continuous/Prismatic, in description order) |
| `DrivableJointCount` | `int` | Number of drivable joints |
| `RootPose` | `Matrix4x4` | Robot root world pose (default `Identity`); all FK is based on it |

Methods:
- `void SetJointValue(string name, float value)` — drive by name (radians rotation / meters translation)
- `float GetJointValue(string name)` — read a drivable joint's current value
- `void ApplyJointValues(IReadOnlyList<float> values)` — batch assign (length must equal `DrivableJointCount`)
- `Matrix4x4 GetLinkGlobalPose(string linkName)` — a link's global pose relative to `RootPose` (updates FK before querying)
- `bool TryGetLinkGlobalPose(string linkName, out Matrix4x4 pose)` — Try version

> Poses are row-major matrices (consistent with `Core.Scene.Transform`); `GetLinkGlobalPose` output can be written directly into `GameObject.Transform` or consumed elsewhere. Not thread-safe: single-threaded use assumed; if pushed across threads, snapshot the state first.

```csharp
using RobotSimulation.Robot;

var state = RobotModel.Parse(File.ReadAllText("robot.urdf")).CreateState();
state.SetJointValue("shoulder", 1.2f);
state.SetJointValue("slider", 0.35f);
Matrix4x4 ee = state.GetLinkGlobalPose("tool0");
```

---

## 5. Key parsing behavior

1. Numbers are always `CultureInfo.InvariantCulture` (URDF decimal point is always `.`).
2. Materials are collected in two passes: first the robot-level and link-embedded `material name→definition`, then resolve visual references by name; later same-name overrides; missing logs a warning and leaves null (renderer uses default color).
3. `rpy` → pose matrix: URDF uses fixed-axis XYZ (R = Rz(yaw)·Ry(pitch)·Rx(roll)); composed into a `Matrix4x4` stored in `Joint.Origin` / `VisualElement.LocalTransform` (row-major, consistent with `Core.Scene.Transform`).
4. Topology validation: duplicate link/joint names, missing referenced links, parent-backtracking cycles are reported; multiple roots are valid ("partial assembly") and not an error.
5. Geometry semantics: cylinder/capsule axis along +Z; dimensions are the URDF size/radius/length, no engine-side scaling.
6. Mesh references are not resolved at this layer: `MeshGeometry.Uri` stores only the raw string, consumed by `Core/Geometry/Import/AssimpModelLoader` and `IAssetResolver`, producing `MeshData` (hung on the visual node by `RobotModel`).

---

## 6. Visibility

- **public (user-facing)**: `RobotModel`, `IAssetResolver`, `FileSystemAssetResolver`, `UrdfLocator`, `UrdfParseException`, all `Description` data models, `RobotState`.
- **internal (implementation detail)**: `UrdfParser` — users enter only via `RobotModel.Parse/ParseFile`.
- The Robot layer exposes no render/GL resources; `RobotModel` construction yields only CPU data (`MeshData`/`MaterialData`); GPU instantiation is done uniformly by the render backend on the render thread.
